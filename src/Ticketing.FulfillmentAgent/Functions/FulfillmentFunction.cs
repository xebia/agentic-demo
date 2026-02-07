using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ticketing.FulfillmentAgent.Models;
using Ticketing.FulfillmentAgent.Services;
using Ticketing.Messaging.Abstractions;

namespace Ticketing.FulfillmentAgent.Functions;

public class FulfillmentFunction
{
    private readonly TicketingApiClient _apiClient;
    private readonly VendorApiClient _vendorClient;
    private readonly IEventPublisher _eventPublisher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FulfillmentFunction> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FulfillmentFunction(
        TicketingApiClient apiClient,
        VendorApiClient vendorClient,
        IEventPublisher eventPublisher,
        IConfiguration configuration,
        ILogger<FulfillmentFunction> logger)
    {
        _apiClient = apiClient;
        _vendorClient = vendorClient;
        _eventPublisher = eventPublisher;
        _configuration = configuration;
        _logger = logger;
    }

    [Function("ProcessFulfillment")]
    public async Task Run(
        [ServiceBusTrigger("tickets.events", "fulfillment-agent-subscription",
            Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received message: {Subject} (MessageId: {MessageId})",
            message.Subject, message.MessageId);

        // Only process fulfillment-requested events
        if (message.Subject != TicketEventTypes.TicketFulfillmentRequested)
        {
            _logger.LogDebug("Ignoring event type {Subject}", message.Subject);
            return;
        }

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

        await FulfillTicketByIdAsync(ticketEvent.Payload.TicketId, cancellationToken);
    }

    internal async Task FulfillTicketByIdAsync(string ticketId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting fulfillment for ticket {TicketId}", ticketId);

        var ticket = await _apiClient.GetTicketAsync(ticketId, cancellationToken);
        if (ticket == null)
        {
            _logger.LogWarning("Ticket {TicketId} not found, skipping", ticketId);
            return;
        }

        if (!string.Equals(ticket.Status, "PendingFulfillment", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Ticket {TicketId} is in status {Status} (not PendingFulfillment), skipping",
                ticketId, ticket.Status);
            return;
        }

        // Parse items from triage notes (the purchasing agent stores quote details there)
        var items = ParseItemsFromTriageNotes(ticket.TriageNotes);
        if (items.Count == 0)
        {
            _logger.LogWarning("No items found in triage notes for ticket {TicketId}, reassigning to Purchasing", ticketId);

            var currentNotes = ticket.TriageNotes ?? "";
            await _apiClient.UpdateTicketAsync(ticketId, new UpdateTicketRequest
            {
                Status = "InProgress",
                AssignedQueue = "Purchasing",
                TriageNotes = $"{currentNotes}\n\n--- Fulfillment ({DateTime.UtcNow:u}) ---\nCould not parse item SKUs from triage notes. Unable to submit order to vendor.\nManual review is needed to add valid SKU references before fulfillment can proceed."
            }, cancellationToken);

            return;
        }

        // Build callback URL
        var callbackBaseUrl = _configuration["TicketingApi:BaseUrl"]
            ?? throw new InvalidOperationException("TicketingApi:BaseUrl is not configured");
        var callbackUrl = $"{callbackBaseUrl}/api/vendor/callback";

        // Submit order to vendor
        var orderRequest = new VendorOrderRequest
        {
            TicketId = ticketId,
            CallbackUrl = callbackUrl,
            Items = items
        };

        var orderResponse = await _vendorClient.SubmitOrderAsync(orderRequest, cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} submitted for ticket {TicketId}, total: {Total:C}",
            orderResponse.OrderId, ticketId, orderResponse.Total);

        // Update ticket with order reference and item details
        var existingNotes = ticket.TriageNotes ?? "";
        var itemDetails = string.Join("\n", orderResponse.Items.Select(i =>
            $"  - {i.Sku}: {i.Name} — ${i.UnitPrice:F2} x {i.Quantity}"));
        await _apiClient.UpdateTicketAsync(ticketId, new UpdateTicketRequest
        {
            TriageNotes = $"{existingNotes}\n\n--- Fulfillment ({DateTime.UtcNow:u}) ---\nOrder ID: {orderResponse.OrderId}\nOrder submitted at: {orderResponse.CreatedAt:u}\nItems:\n{itemDetails}\nTotal: {orderResponse.Total:C}\nStatus: Awaiting vendor processing"
        }, cancellationToken);

        // Publish order-submitted event
        await _eventPublisher.PublishAsync(new TicketEvent
        {
            EventType = TicketEventTypes.TicketOrderSubmitted,
            Payload = new TicketEventPayload
            {
                TicketId = ticketId,
                Title = ticket.Title,
                Status = ticket.Status,
                AssignedQueue = ticket.AssignedQueue,
                ChangedBy = "fulfillment-agent"
            }
        }, cancellationToken);
    }

    private static List<VendorOrderItem> ParseItemsFromTriageNotes(string? triageNotes)
    {
        if (string.IsNullOrWhiteSpace(triageNotes))
            return [];

        // Look for SKU references in the triage notes (format: SKU: XXX-YYY)
        var items = new List<VendorOrderItem>();
        foreach (var line in triageNotes.Split('\n'))
        {
            var trimmed = line.Trim();
            // Match lines like "- LAP-DEV: Developer Laptop" or "SKU: LAP-DEV"
            if (trimmed.StartsWith("- ") && trimmed.Contains(':'))
            {
                var sku = trimmed[2..].Split(':')[0].Trim();
                if (sku.Length > 0 && sku.Contains('-'))
                {
                    items.Add(new VendorOrderItem { Sku = sku, Quantity = 1 });
                }
            }
        }

        return items;
    }
}
