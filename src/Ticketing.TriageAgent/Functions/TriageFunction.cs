using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Ticketing.Messaging.Abstractions;
using Ticketing.Messaging.Abstractions.Diagnostics;
using Ticketing.TriageAgent.Models;
using Ticketing.TriageAgent.Services;

namespace Ticketing.TriageAgent.Functions;

/// <summary>
/// Service Bus trigger that triages new tickets when a ticket.created event is received.
/// </summary>
public class TriageFunction
{
    private readonly TicketingApiClient _apiClient;
    private readonly ITriageService _triageService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<TriageFunction> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TriageFunction(
        TicketingApiClient apiClient,
        ITriageService triageService,
        IEventPublisher eventPublisher,
        ILogger<TriageFunction> logger)
    {
        _apiClient = apiClient;
        _triageService = triageService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    [Function("TriageTicket")]
    public async Task Run(
        [ServiceBusTrigger("tickets.events", "triage-agent-subscription",
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

        using var activity = TicketingTelemetry.Source.StartActivity("triage.process");
        activity?.SetTag("ticket.id", ticketEvent.Payload.TicketId);
        var sw = Stopwatch.StartNew();
        try
        {
            await TriageTicketByIdAsync(ticketEvent.Payload.TicketId, cancellationToken);
            await messageActions.CompleteMessageAsync(message);
            TicketingTelemetry.EventsProcessed.Add(1, new KeyValuePair<string, object?>("agent", "triage"), new KeyValuePair<string, object?>("outcome", "success"));
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogWarning(ex, "Transient error processing ticket {TicketId}, will retry", ticketEvent.Payload.TicketId);
            TicketingTelemetry.EventsProcessed.Add(1, new KeyValuePair<string, object?>("agent", "triage"), new KeyValuePair<string, object?>("outcome", "transient_error"));
            throw; // Let Service Bus retry
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process ticket {TicketId} (delivery {DeliveryCount})", ticketEvent.Payload.TicketId, message.DeliveryCount);
            if (message.DeliveryCount >= 5)
            {
                await messageActions.DeadLetterMessageAsync(message, deadLetterReason: "ProcessingFailed", deadLetterErrorDescription: ex.Message);
                TicketingTelemetry.EventsProcessed.Add(1, new KeyValuePair<string, object?>("agent", "triage"), new KeyValuePair<string, object?>("outcome", "dead_lettered"));
            }
            else
            {
                TicketingTelemetry.EventsProcessed.Add(1, new KeyValuePair<string, object?>("agent", "triage"), new KeyValuePair<string, object?>("outcome", "failed"));
                throw; // Retry
            }
        }
        finally
        {
            sw.Stop();
            TicketingTelemetry.ProcessingDuration.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("agent", "triage"));
        }
    }

    internal async Task TriageTicketByIdAsync(string ticketId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting triage for ticket {TicketId}", ticketId);

        // 1. Fetch the ticket
        var ticket = await _apiClient.GetTicketAsync(ticketId, cancellationToken);
        if (ticket == null)
        {
            _logger.LogWarning("Ticket {TicketId} not found, skipping", ticketId);
            return;
        }

        // 2. Idempotency guard: skip if not "New"
        if (!string.Equals(ticket.Status, "New", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Ticket {TicketId} is in status {Status} (not New), skipping",
                ticketId, ticket.Status);
            return;
        }

        // 3. Call LLM for triage decision
        var decision = await _triageService.TriageTicketAsync(ticket, cancellationToken);

        // 4. Update the ticket via REST API
        var updateRequest = new UpdateTicketRequest
        {
            Status = "Triaged",
            AssignedQueue = decision.Queue,
            TriageDecision = decision.Queue,
            TriageNotes = decision.Reasoning,
            Priority = decision.Priority,
            Category = decision.Category
        };

        var updatedTicket = await _apiClient.UpdateTicketAsync(ticketId, updateRequest, cancellationToken);
        if (updatedTicket == null)
        {
            _logger.LogError("Failed to update ticket {TicketId} after triage", ticketId);
            return;
        }

        _logger.LogInformation(
            "Ticket {TicketId} triaged: Queue={Queue}, Priority={Priority}, Category={Category}",
            ticketId, decision.Queue, decision.Priority, decision.Category);

        // 5. Publish ticket.assigned event
        await _eventPublisher.PublishAsync(new TicketEvent
        {
            EventType = TicketEventTypes.TicketAssigned,
            Payload = new TicketEventPayload
            {
                TicketId = ticketId,
                Title = updatedTicket.Title,
                Status = updatedTicket.Status,
                AssignedQueue = decision.Queue,
                TriageDecision = decision.Queue,
                TriageNotes = decision.Reasoning,
                Priority = decision.Priority,
                Category = decision.Category,
                CreatedBy = updatedTicket.CreatedBy,
                ChangedBy = "triage-agent"
            }
        }, cancellationToken);
    }
}
