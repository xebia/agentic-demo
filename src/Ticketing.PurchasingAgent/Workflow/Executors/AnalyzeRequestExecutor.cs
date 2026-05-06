using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Ticketing.PurchasingAgent.Models;
using Ticketing.PurchasingAgent.Services;

namespace Ticketing.PurchasingAgent.Workflow.Executors;

/// <summary>
/// LLM-driven analysis of the purchase request. Identifies items to buy and an
/// auto-approve recommendation; if no items can be identified, escalates.
/// </summary>
[SendsMessage(typeof(PurchaseContext))]
[SendsMessage(typeof(EscalateRequest))]
public sealed class AnalyzeRequestExecutor : Executor<PurchaseContext>
{
    private readonly IPurchasingService _purchasingService;
    private readonly ILogger<AnalyzeRequestExecutor> _logger;

    public AnalyzeRequestExecutor(
        IPurchasingService purchasingService,
        ILogger<AnalyzeRequestExecutor> logger)
        : base("purchasing.analyze-request")
    {
        _purchasingService = purchasingService;
        _logger = logger;
    }

    public override async ValueTask HandleAsync(
        PurchaseContext message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var decision = await _purchasingService.AnalyzePurchaseRequestAsync(message.Ticket, cancellationToken);

        if (decision.Items.Count == 0)
        {
            _logger.LogWarning("LLM returned no items for ticket {TicketId}", message.TicketId);
            var existingNotes = message.Ticket.TriageNotes ?? "";
            var escalationNotes = $"{existingNotes}\n\n--- Purchasing Analysis ({DateTime.UtcNow:u}) ---\n"
                + "Automated analysis could not identify specific items to purchase from this request.\n"
                + $"Title: {message.Ticket.Title}\nDescription: {message.Ticket.Description}\n\n"
                + "Human review is needed to clarify the purchase requirements.";
            await context.SendMessageAsync(
                new EscalateRequest(message.TicketId, escalationNotes),
                cancellationToken: cancellationToken);
            return;
        }

        await context.SendMessageAsync(
            message with { Analysis = decision },
            cancellationToken: cancellationToken);
    }
}
