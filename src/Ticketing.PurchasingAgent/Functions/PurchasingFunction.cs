using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Ticketing.Messaging.Abstractions;
using Ticketing.Messaging.Abstractions.Diagnostics;
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
        ServiceBusMessageActions messageActions,
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
            _logger.LogError(ex, "Failed to deserialize message body: {Body}", message.Body.ToString());
            await messageActions.DeadLetterMessageAsync(message, deadLetterReason: "DeserializationFailed", deadLetterErrorDescription: ex.Message);
            return;
        }

        if (ticketEvent?.Payload?.TicketId == null)
        {
            _logger.LogWarning("Message has no ticket ID in payload, dead-lettering: {Body}", message.Body.ToString());
            await messageActions.DeadLetterMessageAsync(message, deadLetterReason: "MissingTicketId", deadLetterErrorDescription: "No ticket ID in payload");
            return;
        }

        using var activity = TicketingTelemetry.Source.StartActivity("purchasing.process");
        activity?.SetTag("ticket.id", ticketEvent.Payload.TicketId);
        var sw = Stopwatch.StartNew();
        try
        {
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

            await messageActions.CompleteMessageAsync(message);
            TicketingTelemetry.EventsProcessed.Add(1, new KeyValuePair<string, object?>("agent", "purchasing"), new KeyValuePair<string, object?>("outcome", "success"));
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogWarning(ex, "Transient error processing ticket {TicketId}, will retry", ticketEvent.Payload.TicketId);
            TicketingTelemetry.EventsProcessed.Add(1, new KeyValuePair<string, object?>("agent", "purchasing"), new KeyValuePair<string, object?>("outcome", "transient_error"));
            throw; // Let Service Bus retry
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process ticket {TicketId} (delivery {DeliveryCount})", ticketEvent.Payload.TicketId, message.DeliveryCount);
            if (message.DeliveryCount >= 5)
            {
                await messageActions.DeadLetterMessageAsync(message, deadLetterReason: "ProcessingFailed", deadLetterErrorDescription: ex.Message);
                TicketingTelemetry.EventsProcessed.Add(1, new KeyValuePair<string, object?>("agent", "purchasing"), new KeyValuePair<string, object?>("outcome", "dead_lettered"));
            }
            else
            {
                TicketingTelemetry.EventsProcessed.Add(1, new KeyValuePair<string, object?>("agent", "purchasing"), new KeyValuePair<string, object?>("outcome", "failed"));
                throw; // Retry
            }
        }
        finally
        {
            sw.Stop();
            TicketingTelemetry.ProcessingDuration.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("agent", "purchasing"));
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

        // 2a. Cascade loop protection: escalate to human review after repeated fulfillment failures
        var fulfillmentFailures = ticket.TriageNotes?.Split("--- Vendor Fulfillment Failed").Length - 1 ?? 0;
        if (fulfillmentFailures >= 3)
        {
            _logger.LogWarning("Ticket {TicketId} has failed fulfillment {Count} times, escalating to human review",
                ticketId, fulfillmentFailures);
            await _apiClient.UpdateTicketAsync(ticketId, new UpdateTicketRequest
            {
                Status = "InProgress",
                AssignedQueue = "Helpdesk",
                TriageNotes = (ticket.TriageNotes ?? "") + $"\n\n--- Escalation ({DateTime.UtcNow:u}) ---\nAutomatic escalation: ticket has failed vendor fulfillment {fulfillmentFailures} times. Requires human review to resolve."
            }, cancellationToken);
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
