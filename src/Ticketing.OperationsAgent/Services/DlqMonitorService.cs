using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ticketing.OperationsAgent.Models;

namespace Ticketing.OperationsAgent.Services;

/// <summary>
/// Checks dead-letter queue depths for all agent subscriptions.
/// </summary>
public class DlqMonitorService
{
    private readonly ILogger<DlqMonitorService> _logger;
    private readonly string? _connectionString;

    private static readonly string[] Subscriptions =
    [
        "triage-agent-subscription",
        "purchasing-agent-subscription",
        "fulfillment-agent-subscription",
        "operations-agent-subscription"
    ];

    public DlqMonitorService(IConfiguration configuration, ILogger<DlqMonitorService> logger)
    {
        _logger = logger;
        _connectionString = configuration["ServiceBusConnection"];
    }

    public async Task<List<DlqStatus>> CheckDeadLetterQueuesAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<DlqStatus>();

        if (string.IsNullOrEmpty(_connectionString))
        {
            _logger.LogWarning("ServiceBusConnection not configured, skipping DLQ check");
            return results;
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
                    _logger.LogWarning("DLQ depth for {Subscription}: {Count} messages",
                        subscription, dlqCount);
                }

                results.Add(new DlqStatus
                {
                    SubscriptionName = subscription,
                    MessageCount = dlqCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check DLQ for {Subscription}", subscription);
            }
        }

        return results;
    }
}
