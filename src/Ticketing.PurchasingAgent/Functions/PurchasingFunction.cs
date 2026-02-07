using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Ticketing.Messaging.Abstractions;
using Ticketing.PurchasingAgent.Models;
using Ticketing.PurchasingAgent.Services;

namespace Ticketing.PurchasingAgent.Functions;

/// <summary>
/// Service Bus trigger that handles purchasing workflow events:
/// - ticket.assigned (queue=Purchasing): evaluates and quotes the purchase
/// - ticket.approved: transitions to fulfillment
/// </summary>
public class PurchasingFunction
{
    private readonly TicketingApiClient _apiClient;
    private readonly IPurchasingService _purchasingService;
    private readonly FulfillmentApiClient _fulfillmentClient;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<PurchasingFunction> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const decimal AutoApproveThreshold = 500m;

    public PurchasingFunction(
        TicketingApiClient apiClient,
        IPurchasingService purchasingService,
        FulfillmentApiClient fulfillmentClient,
        IEventPublisher eventPublisher,
        ILogger<PurchasingFunction> logger)
    {
        _apiClient = apiClient;
        _purchasingService = purchasingService;
        _fulfillmentClient = fulfillmentClient;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    [Function("ProcessPurchasing")]
    public async Task Run(
        [ServiceBusTrigger("tickets.events", "purchasing-agent-subscription",
            Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received message: {Subject} (MessageId: {MessageId})",
            message.Subject, message.MessageId);

        TicketEvent? ticketEvent;
        try
        {
            ticketEvent = JsonSerializer.Deserialize<TicketEvent>(message.Body.ToString(), JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize message body");
            return;
        }

        if (ticketEvent?.Payload?.TicketId == null)
        {
            _logger.LogWarning("Message has no ticket ID in payload, skipping");
            return;
        }

        switch (message.Subject)
        {
            case TicketEventTypes.TicketAssigned
                when string.Equals(ticketEvent.Payload.AssignedQueue, "Purchasing", StringComparison.OrdinalIgnoreCase):
                await ProcessPurchaseTicketByIdAsync(ticketEvent.Payload.TicketId, cancellationToken);
                break;

            case TicketEventTypes.TicketApproved:
                await TransitionToFulfillmentAsync(ticketEvent.Payload.TicketId, cancellationToken);
                break;

            default:
                _logger.LogDebug("Ignoring event {Subject} for ticket {TicketId}",
                    message.Subject, ticketEvent.Payload.TicketId);
                break;
        }
    }

    internal async Task ProcessPurchaseTicketByIdAsync(string ticketId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting purchase evaluation for ticket {TicketId}", ticketId);

        // 1. Fetch the ticket
        var ticket = await _apiClient.GetTicketAsync(ticketId, cancellationToken);
        if (ticket == null)
        {
            _logger.LogWarning("Ticket {TicketId} not found, skipping", ticketId);
            return;
        }

        // 2. Idempotency guard: only process Triaged tickets
        if (!string.Equals(ticket.Status, "Triaged", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Ticket {TicketId} is in status {Status} (not Triaged), skipping",
                ticketId, ticket.Status);
            return;
        }

        // 3. Call LLM to analyze the request
        var decision = await _purchasingService.AnalyzePurchaseRequestAsync(ticket, cancellationToken);

        if (decision.Items.Count == 0)
        {
            _logger.LogWarning("LLM returned no items for ticket {TicketId}, updating ticket for human review", ticketId);

            var existingNotes = ticket.TriageNotes ?? "";
            await _apiClient.UpdateTicketAsync(ticketId, new UpdateTicketRequest
            {
                Status = "InProgress",
                TriageNotes = $"{existingNotes}\n\n--- Purchasing Analysis ({DateTime.UtcNow:u}) ---\nAutomated analysis could not identify specific items to purchase from this request.\nTitle: {ticket.Title}\nDescription: {ticket.Description}\n\nHuman review is needed to clarify the purchase requirements."
            }, cancellationToken);

            return;
        }

        // 4. Get quote from Fulfillment Agent
        var quoteRequest = new QuoteRequest
        {
            TicketId = ticketId,
            Items = decision.Items.Select(i => i.Description).ToList()
        };

        var quote = await _fulfillmentClient.GetQuoteAsync(quoteRequest, cancellationToken);

        _logger.LogInformation(
            "Quote for ticket {TicketId}: {Total:C}, available={Available}",
            ticketId, quote.TotalEstimate, quote.Available);

        // 5. Build triage notes with quote details
        var quoteDetails = string.Join("\n", quote.LineItems.Select(i =>
            $"- {i.Sku}: {i.Name} — ${i.UnitPrice:F2} x {i.Quantity}{(i.Available ? "" : " (UNAVAILABLE)")}"));

        // 5a. If quote is unavailable, update ticket and return early
        if (!quote.Available)
        {
            _logger.LogWarning("Quote unavailable for ticket {TicketId}, items cannot be fulfilled", ticketId);

            var unavailableItems = quote.LineItems
                .Where(i => !i.Available)
                .Select(i => i.Name)
                .ToList();

            var existingNotes = ticket.TriageNotes ?? "";
            await _apiClient.UpdateTicketAsync(ticketId, new UpdateTicketRequest
            {
                Status = "InProgress",
                TriageNotes = $"{existingNotes}\n\n--- Purchasing Analysis ({DateTime.UtcNow:u}) ---\n{decision.Reasoning}\n\nQuote Details:\n{quoteDetails}\n\nUnavailable items: {string.Join(", ", unavailableItems)}\nThe requested items are not available from the vendor catalog. Human review is needed to find alternatives or cancel the request."
            }, cancellationToken);

            return;
        }

        var notes = $"""
            --- Purchasing Analysis ---
            {decision.Reasoning}

            Quote Details:
            {quoteDetails}
            Total: ${quote.TotalEstimate:F2}

            Auto-approve recommendation: {(decision.AutoApproveRecommendation ? "Yes" : "No")}
            """;

        // 6. Decide: auto-approve or require manager approval
        var autoApprove = quote.TotalEstimate <= AutoApproveThreshold && decision.AutoApproveRecommendation;

        if (autoApprove)
        {
            _logger.LogInformation("Auto-approving ticket {TicketId} (total: {Total:C})", ticketId, quote.TotalEstimate);

            await _apiClient.UpdateTicketAsync(ticketId, new UpdateTicketRequest
            {
                Status = "Approved",
                TriageNotes = notes + "\n\nDecision: AUTO-APPROVED (within $500 threshold and standard equipment)"
            }, cancellationToken);

            // Auto-approved tickets go straight to fulfillment
            await TransitionToFulfillmentAsync(ticketId, cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "Ticket {TicketId} requires manager approval (total: {Total:C}, autoApproveRec: {Rec})",
                ticketId, quote.TotalEstimate, decision.AutoApproveRecommendation);

            await _apiClient.UpdateTicketAsync(ticketId, new UpdateTicketRequest
            {
                Status = "PendingApproval",
                TriageNotes = notes + $"\n\nDecision: REQUIRES MANAGER APPROVAL (total ${quote.TotalEstimate:F2} exceeds $500 threshold or non-standard equipment)"
            }, cancellationToken);

            await _eventPublisher.PublishAsync(new TicketEvent
            {
                EventType = TicketEventTypes.TicketApprovalRequired,
                Payload = new TicketEventPayload
                {
                    TicketId = ticketId,
                    Title = ticket.Title,
                    Status = "PendingApproval",
                    AssignedQueue = "Purchasing",
                    ChangedBy = "purchasing-agent"
                }
            }, cancellationToken);
        }
    }

    private async Task TransitionToFulfillmentAsync(string ticketId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Transitioning ticket {TicketId} to fulfillment", ticketId);

        var ticket = await _apiClient.GetTicketAsync(ticketId, cancellationToken);
        if (ticket == null)
        {
            _logger.LogWarning("Ticket {TicketId} not found", ticketId);
            return;
        }

        // Guard: only transition Approved tickets
        if (!string.Equals(ticket.Status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Ticket {TicketId} is in status {Status} (not Approved), skipping fulfillment transition",
                ticketId, ticket.Status);
            return;
        }

        await _apiClient.UpdateTicketAsync(ticketId, new UpdateTicketRequest
        {
            Status = "PendingFulfillment",
            AssignedQueue = "Fulfillment"
        }, cancellationToken);

        await _eventPublisher.PublishAsync(new TicketEvent
        {
            EventType = TicketEventTypes.TicketFulfillmentRequested,
            Payload = new TicketEventPayload
            {
                TicketId = ticketId,
                Title = ticket.Title,
                Status = "PendingFulfillment",
                AssignedQueue = "Fulfillment",
                ChangedBy = "purchasing-agent"
            }
        }, cancellationToken);

        _logger.LogInformation("Ticket {TicketId} moved to Fulfillment queue", ticketId);
    }
}
