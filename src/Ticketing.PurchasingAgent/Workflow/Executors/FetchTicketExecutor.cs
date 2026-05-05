using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Ticketing.PurchasingAgent.Models;
using Ticketing.PurchasingAgent.Services;

namespace Ticketing.PurchasingAgent.Workflow.Executors;

/// <summary>
/// Workflow entry point. Fetches the ticket, applies idempotency and loop-protection
/// guards, and routes to the analysis path, an escalation, or a clean skip.
/// </summary>
public sealed class FetchTicketExecutor : Executor<PurchaseStart>
{
    private readonly TicketingApiClient _apiClient;
    private readonly ILogger<FetchTicketExecutor> _logger;

    public FetchTicketExecutor(
        TicketingApiClient apiClient,
        ILogger<FetchTicketExecutor> logger)
        : base("purchasing.fetch-ticket")
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public override async ValueTask HandleAsync(
        PurchaseStart message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _apiClient.GetTicketAsync(message.TicketId, cancellationToken);
        if (ticket == null)
        {
            _logger.LogWarning("Ticket {TicketId} not found, skipping", message.TicketId);
            await context.YieldOutputAsync(new SkipResult(message.TicketId, "Ticket not found"), cancellationToken);
            await context.RequestHaltAsync();
            return;
        }

        if (!string.Equals(ticket.Status, "Triaged", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Ticket {TicketId} is in status {Status} (not Triaged), skipping",
                message.TicketId, ticket.Status);
            await context.YieldOutputAsync(
                new SkipResult(message.TicketId, $"Status is {ticket.Status}, not Triaged"),
                cancellationToken);
            await context.RequestHaltAsync();
            return;
        }

        var fulfillmentFailures = ticket.TriageNotes?.Split("--- Vendor Fulfillment Failed").Length - 1 ?? 0;
        if (fulfillmentFailures >= 3)
        {
            _logger.LogWarning(
                "Ticket {TicketId} has failed fulfillment {Count} times, escalating",
                message.TicketId, fulfillmentFailures);
            var notes = (ticket.TriageNotes ?? "")
                + $"\n\n--- Escalation ({DateTime.UtcNow:u}) ---\n"
                + $"Automatic escalation: ticket has failed vendor fulfillment {fulfillmentFailures} times. "
                + "Requires human review to resolve.";
            await context.SendMessageAsync(
                new EscalateRequest(message.TicketId, notes),
                cancellationToken: cancellationToken);
            return;
        }

        await context.SendMessageAsync(
            new PurchaseContext(message.TicketId, ticket, Analysis: null, Quote: null),
            cancellationToken: cancellationToken);
    }
}
