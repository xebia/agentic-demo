using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Ticketing.PurchasingAgent.Models;
using Ticketing.PurchasingAgent.Services;

namespace Ticketing.PurchasingAgent.Workflow.Executors;

/// <summary>
/// Terminal executor for human review escalations (no items identified, items
/// unavailable, repeated fulfillment failures). Updates the ticket back to
/// InProgress in the Helpdesk queue with explanatory notes.
/// </summary>
public sealed class EscalateExecutor : Executor<EscalateRequest>
{
    private readonly TicketingApiClient _apiClient;
    private readonly ILogger<EscalateExecutor> _logger;

    public EscalateExecutor(
        TicketingApiClient apiClient,
        ILogger<EscalateExecutor> logger)
        : base("purchasing.escalate")
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public override async ValueTask HandleAsync(
        EscalateRequest message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        await _apiClient.UpdateTicketAsync(message.TicketId, new UpdateTicketRequest
        {
            Status = "InProgress",
            AssignedQueue = "Helpdesk",
            TriageNotes = message.Notes
        }, cancellationToken);

        _logger.LogInformation("Ticket {TicketId} escalated to Helpdesk for human review", message.TicketId);
        await context.RequestHaltAsync();
    }
}
