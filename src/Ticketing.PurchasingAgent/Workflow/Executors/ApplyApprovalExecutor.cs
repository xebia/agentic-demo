using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Ticketing.Messaging.Abstractions;
using Ticketing.PurchasingAgent.Models;
using Ticketing.PurchasingAgent.Services;

namespace Ticketing.PurchasingAgent.Workflow.Executors;

/// <summary>
/// Terminal executor on the approval branch. Applies the approved/rejected outcome
/// to the ticket and, if approved, transitions to the Fulfillment queue and
/// publishes the fulfillment event.
/// </summary>
public sealed class ApplyApprovalExecutor : Executor<ApprovalResolved>
{
    private readonly TicketingApiClient _apiClient;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ApplyApprovalExecutor> _logger;

    public ApplyApprovalExecutor(
        TicketingApiClient apiClient,
        IEventPublisher eventPublisher,
        ILogger<ApplyApprovalExecutor> logger)
        : base("purchasing.apply-approval")
    {
        _apiClient = apiClient;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public override async ValueTask HandleAsync(
        ApprovalResolved message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var ctx = message.Context;
        var quote = ctx.Quote
            ?? throw new InvalidOperationException("ApplyApprovalExecutor invoked without a quote");
        var analysis = ctx.Analysis
            ?? throw new InvalidOperationException("ApplyApprovalExecutor invoked without an analysis");

        var quoteDetails = string.Join("\n", quote.LineItems.Select(i =>
            $"- {i.Sku}: {i.Name} — ${i.UnitPrice:F2} x {i.Quantity}"));

        var notes = $"""
            --- Purchasing Analysis ---
            {analysis.Reasoning}

            Quote Details:
            {quoteDetails}
            Total: ${quote.TotalEstimate:F2}

            Auto-approve recommendation: {(analysis.AutoApproveRecommendation ? "Yes" : "No")}
            """;

        if (message.Approved)
        {
            var approverSuffix = string.IsNullOrEmpty(message.ApproverName)
                ? ""
                : $" by {message.ApproverName}";

            await _apiClient.UpdateTicketAsync(ctx.TicketId, new UpdateTicketRequest
            {
                Status = "Approved",
                TriageNotes = notes + $"\n\nDecision: APPROVED{approverSuffix} — {message.Reason}"
            }, cancellationToken);

            await _apiClient.UpdateTicketAsync(ctx.TicketId, new UpdateTicketRequest
            {
                Status = "PendingFulfillment",
                AssignedQueue = "Fulfillment"
            }, cancellationToken);

            await _eventPublisher.PublishAsync(new TicketEvent
            {
                EventType = TicketEventTypes.TicketFulfillmentRequested,
                Payload = new TicketEventPayload
                {
                    TicketId = ctx.TicketId,
                    Title = ctx.Ticket.Title,
                    Status = "PendingFulfillment",
                    AssignedQueue = "Fulfillment",
                    ChangedBy = "purchasing-agent"
                }
            }, cancellationToken);

            _logger.LogInformation(
                "Ticket {TicketId} approved and transitioned to Fulfillment queue",
                ctx.TicketId);
        }
        else
        {
            await _apiClient.UpdateTicketAsync(ctx.TicketId, new UpdateTicketRequest
            {
                Status = "Rejected",
                TriageNotes = notes + $"\n\nDecision: REJECTED by {message.ApproverName ?? "manager"} — {message.Reason}"
            }, cancellationToken);

            _logger.LogInformation("Ticket {TicketId} rejected: {Reason}", ctx.TicketId, message.Reason);
        }

        await context.RequestHaltAsync();
    }
}
