using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Ticketing.Messaging.Abstractions;
using Ticketing.Messaging.Abstractions.Diagnostics;
using Ticketing.PurchasingAgent.Services;
using Ticketing.PurchasingAgent.Workflow;

namespace Ticketing.PurchasingAgent.Functions;

/// <summary>
/// Single Service Bus trigger that drives the MAF purchasing workflow:
/// - ticket.assigned (queue=Purchasing): runs the workflow from start. If it
///   suspends at the human-approval RequestPort, the runtime checkpoints the
///   state to blob storage (via BlobWorkflowCheckpointStore) and the function
///   exits cleanly.
/// - ticket.approved / ticket.rejected: rehydrates the workflow from the
///   latest checkpoint blob and feeds the human's decision into the
///   suspended RequestPort. Workflow runs to completion; checkpoint blobs are
///   deleted.
/// </summary>
public class PurchasingFunction
{
    private readonly TicketingApiClient _apiClient;
    private readonly PurchasingWorkflowFactory _workflowFactory;
    private readonly BlobWorkflowCheckpointStore _checkpointStore;
    private readonly CheckpointManager _checkpointManager;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<PurchasingFunction> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PurchasingFunction(
        TicketingApiClient apiClient,
        PurchasingWorkflowFactory workflowFactory,
        BlobWorkflowCheckpointStore checkpointStore,
        CheckpointManager checkpointManager,
        IEventPublisher eventPublisher,
        ILogger<PurchasingFunction> logger)
    {
        _apiClient = apiClient;
        _workflowFactory = workflowFactory;
        _checkpointStore = checkpointStore;
        _checkpointManager = checkpointManager;
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
                    await RunWorkflowAsync(ticketEvent.Payload.TicketId, cancellationToken);
                    break;

                case TicketEventTypes.TicketApproved:
                    await ResumeWorkflowAsync(
                        ticketEvent.Payload.TicketId,
                        approved: true,
                        approverName: ticketEvent.Payload.ChangedBy,
                        cancellationToken);
                    break;

                case TicketEventTypes.TicketRejected:
                    await ResumeWorkflowAsync(
                        ticketEvent.Payload.TicketId,
                        approved: false,
                        approverName: ticketEvent.Payload.ChangedBy,
                        cancellationToken);
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
    /// Used by StartupScanFunction to recover any Triaged+Purchasing tickets
    /// that were missed (e.g., a ticket created while the agent was down).
    /// </summary>
    internal Task ProcessPurchaseTicketByIdAsync(string ticketId, CancellationToken cancellationToken)
        => RunWorkflowAsync(ticketId, cancellationToken);

    private async Task RunWorkflowAsync(string ticketId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting purchase workflow for ticket {TicketId}", ticketId);

        var workflow = _workflowFactory.Build();

        await using var run = await InProcessExecution.RunStreamingAsync(
            workflow,
            new PurchaseStart(ticketId),
            _checkpointManager,
            sessionId: ticketId,
            cancellationToken: cancellationToken);

        await foreach (var evt in run.WatchStreamAsync(cancellationToken))
        {
            switch (evt)
            {
                case RequestInfoEvent reqEvt
                    when reqEvt.Request.PortInfo.PortId == WorkflowIds.ApprovalPort:
                    _logger.LogInformation(
                        "Ticket {TicketId} workflow suspended awaiting human approval — checkpoint persisted, exiting until ticket.approved/rejected fires",
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

        // Workflow ran to completion (auto-approve or escalation path) — clean up.
        _logger.LogInformation("Purchase workflow for ticket {TicketId} completed; deleting any checkpoint state", ticketId);
        await _checkpointStore.DeleteSessionAsync(ticketId, cancellationToken);
    }

    private async Task ResumeWorkflowAsync(
        string ticketId,
        bool approved,
        string? approverName,
        CancellationToken cancellationToken)
    {
        var checkpoint = await _checkpointStore.TryGetLatestAsync(ticketId, cancellationToken);
        if (checkpoint is null)
        {
            // No suspended workflow. Either the ticket auto-approved (workflow already
            // ran to completion and cleaned its blobs) or someone PATCHed status
            // directly. Fall back to the legacy direct transition so direct-API
            // approvals still flow to fulfillment.
            if (approved)
            {
                _logger.LogInformation(
                    "No checkpoint for ticket {TicketId} — falling back to direct fulfillment transition",
                    ticketId);
                await TransitionToFulfillmentAsync(ticketId, cancellationToken);
            }
            else
            {
                _logger.LogDebug("No checkpoint for ticket {TicketId} on rejection, nothing to do", ticketId);
            }
            return;
        }

        _logger.LogInformation(
            "Resuming purchase workflow for ticket {TicketId} from checkpoint {CheckpointId} ({Decision})",
            ticketId, checkpoint.CheckpointId, approved ? "approved" : "rejected");

        // Resume reason for rejection comes from the ticket's resolution notes,
        // which the approver UI populates via TicketEdit.Reject(reason).
        string? reason = null;
        if (!approved)
        {
            var ticket = await _apiClient.GetTicketAsync(ticketId, cancellationToken);
            const string prefix = "Rejected: ";
            if (ticket?.ResolutionNotes is { } notes && notes.StartsWith(prefix))
            {
                reason = notes[prefix.Length..];
            }
        }

        var workflow = _workflowFactory.Build();

        await using var run = await InProcessExecution.ResumeStreamingAsync(
            workflow,
            checkpoint,
            _checkpointManager,
            cancellationToken);

        var responded = false;

        await foreach (var evt in run.WatchStreamAsync(cancellationToken))
        {
            switch (evt)
            {
                case RequestInfoEvent reqEvt
                    when !responded && reqEvt.Request.PortInfo.PortId == WorkflowIds.ApprovalPort:
                    var response = reqEvt.Request.CreateResponse(
                        new ApprovalResponse(approved, reason, approverName));
                    await run.SendResponseAsync(response);
                    responded = true;
                    break;
                case ExecutorFailedEvent failedEvt:
                    _logger.LogError("Executor {Id} failed during resume: {Reason}", failedEvt.ExecutorId, failedEvt.ToString());
                    break;
                case WorkflowErrorEvent errEvt:
                    _logger.LogError("Workflow error during resume: {Message}", errEvt.ToString());
                    break;
            }
        }

        if (!responded)
        {
            _logger.LogWarning(
                "Workflow for ticket {TicketId} did not re-emit approval RequestInfoEvent on resume — checkpoint may be stale",
                ticketId);
        }

        await _checkpointStore.DeleteSessionAsync(ticketId, cancellationToken);
        _logger.LogInformation("Purchase workflow for ticket {TicketId} resumed and completed", ticketId);
    }

    /// <summary>
    /// Fallback path for ticket.approved events that don't have a suspended
    /// workflow (e.g., direct API patches that bypass the approver UI).
    /// </summary>
    private async Task TransitionToFulfillmentAsync(string ticketId, CancellationToken cancellationToken)
    {
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

        await _apiClient.UpdateTicketAsync(ticketId, new Models.UpdateTicketRequest
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

        _logger.LogInformation("Ticket {TicketId} moved to Fulfillment queue (fallback path)", ticketId);
    }
}
