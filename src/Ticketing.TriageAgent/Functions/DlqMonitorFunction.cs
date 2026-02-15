using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ticketing.Messaging.Abstractions.Diagnostics;

namespace Ticketing.TriageAgent.Functions;

public class DlqMonitorFunction
{
    private readonly ILogger<DlqMonitorFunction> _logger;
    private readonly string? _connectionString;

    private static readonly string[] Subscriptions =
    [
        "triage-agent-subscription",
        "purchasing-agent-subscription",
        "fulfillment-agent-subscription"
    ];

    public DlqMonitorFunction(IConfiguration configuration, ILogger<DlqMonitorFunction> logger)
    {
        _logger = logger;
        _connectionString = configuration["ServiceBusConnection"];
    }

    [Function("DlqMonitor")]
    public async Task Run(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            _logger.LogWarning("ServiceBusConnection not configured, skipping DLQ monitor");
            return;
        }

        var adminClient = new ServiceBusAdministrationClient(_connectionString);

        foreach (var subscription in Subscriptions)
        {
            try
            {
                var properties = await adminClient.GetSubscriptionRuntimePropertiesAsync(
                    "tickets.events", subscription, cancellationToken);
                var dlqCount = properties.Value.DeadLetterMessageCount;

                if (dlqCount > 0)
                {
                    _logger.LogWarning(
                        "DLQ depth for {Subscription}: {Count} messages",
                        subscription, dlqCount);
                }

                TicketingTelemetry.Meter.CreateObservableGauge(
                    $"messaging.dlq.depth.{subscription}",
                    () => dlqCount,
                    description: $"Dead letter queue depth for {subscription}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check DLQ for {Subscription}", subscription);
            }
        }
    }
}
