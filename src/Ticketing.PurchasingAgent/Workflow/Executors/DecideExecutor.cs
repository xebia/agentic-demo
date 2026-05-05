using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Ticketing.Messaging.Abstractions;
using Ticketing.PurchasingAgent.Models;
using Ticketing.PurchasingAgent.Services;

namespace Ticketing.PurchasingAgent.Workflow.Executors;

/// <summary>
/// Branches the workflow:
/// - auto-approve: ticket total ≤ $500 AND LLM recommended auto-approve → emits ApprovalResolved directly
/// - human approval: anything else → stashes context in shared state and emits ApprovalRequest to the RequestPort (suspends)
/// </summary>
public sealed class DecideExecutor : Executor<PurchaseContext>
{
    public const string ContextStateScope = "purchasing.context";
    public const string ContextStateKey = "current";
    public const decimal AutoApproveThreshold = 500m;

    private readonly TicketingApiClient _apiClient;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<DecideExecutor> _logger;

    public DecideExecutor(
        TicketingApiClient apiClient,
        IEventPublisher eventPublisher,
        ILogger<DecideExecutor> logger)
        : base("purchasing.decide")
    {
        _apiClient = apiClient;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public override async ValueTask HandleAsync(
        PurchaseContext message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (message.Analysis is null || message.Quote is null)
        {
            throw new InvalidOperationException("DecideExecutor invoked without analysis or quote");
        }

        var autoApprove =
            message.Quote.TotalEstimate <= AutoApproveThreshold
            && message.Analysis.AutoApproveRecommendation;

        if (autoApprove)
        {
            _logger.LogInformation(
                "Auto-approving ticket {TicketId} (total: {Total:C})",
                message.TicketId, message.Quote.TotalEstimate);

            await context.SendMessageAsync(
                new ApprovalResolved(message, Approved: true, Reason: "Auto-approved", ApproverName: null),
                cancellationToken: cancellationToken);
            return;
        }

        _logger.LogInformation(
            "Ticket {TicketId} requires manager approval (total: {Total:C}, autoApproveRec: {Rec})",
            message.TicketId, message.Quote.TotalEstimate, message.Analysis.AutoApproveRecommendation);

        // Move the ticket to PendingApproval and announce that approval is needed.
        // This must happen before suspending so the approver UI sees the request.
        var quoteDetails = string.Join("\n", message.Quote.LineItems.Select(i =>
            $"- {i.Sku}: {i.Name} — ${i.UnitPrice:F2} x {i.Quantity}"));
        var notes = $"""
            --- Purchasing Analysis ---
            {message.Analysis.Reasoning}

            Quote Details:
            {quoteDetails}
            Total: ${message.Quote.TotalEstimate:F2}

            Auto-approve recommendation: {(message.Analysis.AutoApproveRecommendation ? "Yes" : "No")}

            Decision: REQUIRES MANAGER APPROVAL (total ${message.Quote.TotalEstimate:F2} exceeds $500 threshold or non-standard equipment)
            """;
        await _apiClient.UpdateTicketAsync(message.TicketId, new UpdateTicketRequest
        {
            Status = "PendingApproval",
            TriageNotes = notes
        }, cancellationToken);

        await _eventPublisher.PublishAsync(new TicketEvent
        {
            EventType = TicketEventTypes.TicketApprovalRequired,
            Payload = new TicketEventPayload
            {
                TicketId = message.TicketId,
                Title = message.Ticket.Title,
                Status = "PendingApproval",
                AssignedQueue = "Purchasing",
                ChangedBy = "purchasing-agent"
            }
        }, cancellationToken);

        // Persist context to shared workflow state so the bridge executor can
        // recover it after the RequestPort returns a response (after the human acts).
        await context.QueueStateUpdateAsync(ContextStateKey, message, ContextStateScope, cancellationToken);

        var request = new ApprovalRequest(
            message.TicketId,
            message.Ticket.Title,
            message.Quote.TotalEstimate,
            message.Analysis.Reasoning,
            message.Quote.LineItems);

        await context.SendMessageAsync(request, cancellationToken: cancellationToken);
    }
}
