using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Ticketing.OperationsAgent.Models;
using Ticketing.OperationsAgent.Services;

namespace Ticketing.OperationsAgent.Functions;

public class EventAnomalyFunction
{
    private readonly AlertApiClient _alertApiClient;
    private readonly ILogger<EventAnomalyFunction> _logger;

    private static readonly ConcurrentQueue<DateTime> SlidingWindow = new();
    private const int BurstThreshold = 20;
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(1);

    public EventAnomalyFunction(
        AlertApiClient alertApiClient,
        ILogger<EventAnomalyFunction> logger)
    {
        _alertApiClient = alertApiClient;
        _logger = logger;
    }

    [Function("EventAnomaly")]
    public async Task Run(
        [ServiceBusTrigger("tickets.events", "operations-agent-subscription",
            Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // Add current event timestamp
        SlidingWindow.Enqueue(now);

        // Remove entries older than the window
        while (SlidingWindow.TryPeek(out var oldest) && (now - oldest) > WindowDuration)
        {
            SlidingWindow.TryDequeue(out _);
        }

        var count = SlidingWindow.Count;

        _logger.LogDebug(
            "Event received: {Subject}, sliding window count: {Count}/{Threshold}",
            message.Subject, count, BurstThreshold);

        if (count > BurstThreshold)
        {
            _logger.LogWarning(
                "Event burst detected: {Count} events in the last minute (threshold: {Threshold})",
                count, BurstThreshold);

            var alert = new OperationsAlert
            {
                Severity = "Warning",
                Title = "Event burst detected",
                Description = $"{count} events received in the last minute (threshold: {BurstThreshold}). This may indicate an automated loop or system issue.",
                Remediation = "Check agent logs for retry loops or cascading event patterns",
                Source = "EventAnomaly"
            };

            await _alertApiClient.PostAlertAsync(alert, cancellationToken);
        }

        // Always complete — monitoring should never dead-letter
        await messageActions.CompleteMessageAsync(message, cancellationToken);
    }
}
