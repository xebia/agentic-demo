using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Ticketing.Messaging.Abstractions;
using Ticketing.Messaging.Abstractions.Diagnostics;
using Ticketing.PurchasingAgent.Models;
using Ticketing.PurchasingAgent.Services;
using Ticketing.PurchasingAgent.Workflow;

namespace Ticketing.PurchasingAgent.Functions;

/// <summary>
/// Service Bus trigger that handles purchasing workflow events:
/// - ticket.assigned (queue=Purchasing): runs the MAF purchasing workflow
/// - ticket.approved: transitions an already-approved ticket to fulfillment
///   (kept for backward compatibility until Stage 2 introduces resume)
/// </summary>
public class PurchasingFunction
{
    private readonly TicketingApiClient _apiClient;
    private readonly PurchasingWorkflowFactory _workflowFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<PurchasingFunction> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PurchasingFunction(
        TicketingApiClient apiClient,
        PurchasingWorkflowFactory workflowFactory,
        IEventPublisher eventPublisher,
        ILogger<PurchasingFunction> logger)
    {
        _apiClient = apiClient;
        _workflowFactory = workflowFactory;
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
            throw;
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
                throw;
            }
        }
        finally
        {
            sw.Stop();
            TicketingTelemetry.ProcessingDuration.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("agent", "purchasing"));
        }
    }

    /// <summary>
    /// Drives the MAF purchasing workflow for a single ticket. Auto-approve and
    /// escalation paths run to completion. The human-approval path runs to its
    /// suspension point at the RequestPort and then exits cleanly — Stage 2
    /// adds checkpointing and a separate resume function.
    /// </summary>
    internal async Task ProcessPurchaseTicketByIdAsync(string ticketId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting purchase workflow for ticket {TicketId}", ticketId);

        var workflow = _workflowFactory.Build();

        await using var run = await InProcessExecution.RunStreamingAsync(
            workflow,
            new PurchaseStart(ticketId),
            sessionId: ticketId,
            cancellationToken: cancellationToken);

        await foreach (var evt in run.WatchStreamAsync(cancellationToken))
        {
            switch (evt)
            {
                case RequestInfoEvent reqEvt
                    when reqEvt.Request.PortInfo.PortId == WorkflowIds.ApprovalPort:
                    _logger.LogInformation(
                        "Ticket {TicketId} workflow suspended awaiting human approval — exiting until ticket.approved/rejected fires",
                        ticketId);
                    return;
                case ExecutorFailedEvent failedEvt:
                    _logger.LogError("Executor {Id} failed: {Reason}", failedEvt.ExecutorId, failedEvt.ToString());
                    break;
                case WorkflowErrorEvent errEvt:
                    _logger.LogError("Workflow error: {Message}", errEvt.ToString());
                    break;
            }
        }

        _logger.LogInformation("Purchase workflow for ticket {TicketId} completed", ticketId);
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
