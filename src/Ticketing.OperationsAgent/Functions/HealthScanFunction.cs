using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Ticketing.OperationsAgent.Models;
using Ticketing.OperationsAgent.Services;

namespace Ticketing.OperationsAgent.Functions;

public class HealthScanFunction
{
    private readonly HealthCheckService _healthCheckService;
    private readonly DlqMonitorService _dlqMonitorService;
    private readonly TicketingApiClient _ticketingApiClient;
    private readonly IOperationsAnalyzer _operationsAnalyzer;
    private readonly AlertApiClient _alertApiClient;
    private readonly ILogger<HealthScanFunction> _logger;

    // SLA thresholds per status
    private static readonly TimeSpan NewTicketThreshold = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan NewCriticalTicketThreshold = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TriagedThreshold = TimeSpan.FromHours(2);
    private static readonly TimeSpan PendingApprovalThreshold = TimeSpan.FromHours(1);
    private static readonly TimeSpan PendingFulfillmentThreshold = TimeSpan.FromHours(4);

    public HealthScanFunction(
        HealthCheckService healthCheckService,
        DlqMonitorService dlqMonitorService,
        TicketingApiClient ticketingApiClient,
        IOperationsAnalyzer operationsAnalyzer,
        AlertApiClient alertApiClient,
        ILogger<HealthScanFunction> logger)
    {
        _healthCheckService = healthCheckService;
        _dlqMonitorService = dlqMonitorService;
        _ticketingApiClient = ticketingApiClient;
        _operationsAnalyzer = operationsAnalyzer;
        _alertApiClient = alertApiClient;
        _logger = logger;
    }

    [Function("HealthScan")]
    public async Task Run(
        [TimerTrigger("0 */2 * * * *")] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting health scan");

        var scanResult = new HealthScanResult();

        // 1. Check service health
        scanResult.Services = await _healthCheckService.CheckServicesAsync(cancellationToken);

        // 2. Check DLQ depths
        scanResult.DeadLetterQueues = await _dlqMonitorService.CheckDeadLetterQueuesAsync(cancellationToken);

        // 3. Check SLA thresholds
        scanResult.SlaViolations = await CheckSlaViolationsAsync(cancellationToken);

        // Determine if there are any issues
        var hasUnhealthyServices = scanResult.Services.Any(s => !s.IsHealthy);
        var hasDlqMessages = scanResult.DeadLetterQueues.Any(d => d.MessageCount > 0);
        var hasSlaViolations = scanResult.SlaViolations.Count > 0;

        if (!hasUnhealthyServices && !hasDlqMessages && !hasSlaViolations)
        {
            _logger.LogInformation("Health scan complete — all systems healthy");
            return;
        }

        _logger.LogWarning(
            "Issues detected: {UnhealthyServices} unhealthy services, {DlqCount} DLQ subscriptions with messages, {SlaViolations} SLA violations",
            scanResult.Services.Count(s => !s.IsHealthy),
            scanResult.DeadLetterQueues.Count(d => d.MessageCount > 0),
            scanResult.SlaViolations.Count);

        // 4. Analyze with LLM
        List<OperationsAlert> alerts;
        try
        {
            alerts = await _operationsAnalyzer.AnalyzeHealthScanAsync(scanResult, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM analysis failed, generating fallback alerts");
            alerts = GenerateFallbackAlerts(scanResult);
        }

        // 5. Post alerts
        foreach (var alert in alerts)
        {
            await _alertApiClient.PostAlertAsync(alert, cancellationToken);
        }

        _logger.LogInformation("Health scan complete — posted {AlertCount} alerts", alerts.Count);
    }

    private async Task<List<SlaViolation>> CheckSlaViolationsAsync(CancellationToken cancellationToken)
    {
        var violations = new List<SlaViolation>();
        var now = DateTime.UtcNow;

        // Check new tickets
        var newTickets = await _ticketingApiClient.GetNewTicketsAsync(cancellationToken);
        foreach (var ticket in newTickets)
        {
            var age = now - ticket.CreatedAt;
            var threshold = ticket.Priority == "Critical" ? NewCriticalTicketThreshold : NewTicketThreshold;

            if (age > threshold)
            {
                violations.Add(new SlaViolation
                {
                    TicketId = ticket.TicketId,
                    Title = ticket.Title,
                    Status = ticket.Status,
                    Priority = ticket.Priority,
                    Age = age,
                    Threshold = threshold
                });
            }
        }

        // Check triaged tickets
        var triagedTickets = await _ticketingApiClient.GetTriagedTicketsAsync(cancellationToken);
        foreach (var ticket in triagedTickets)
        {
            var age = now - ticket.UpdatedAt;
            if (age > TriagedThreshold)
            {
                violations.Add(new SlaViolation
                {
                    TicketId = ticket.TicketId,
                    Title = ticket.Title,
                    Status = ticket.Status,
                    Priority = ticket.Priority,
                    Age = age,
                    Threshold = TriagedThreshold
                });
            }
        }

        // Check pending approval tickets
        var pendingApproval = await _ticketingApiClient.GetPendingApprovalTicketsAsync(cancellationToken);
        foreach (var ticket in pendingApproval)
        {
            var age = now - ticket.UpdatedAt;
            if (age > PendingApprovalThreshold)
            {
                violations.Add(new SlaViolation
                {
                    TicketId = ticket.TicketId,
                    Title = ticket.Title,
                    Status = ticket.Status,
                    Priority = ticket.Priority,
                    Age = age,
                    Threshold = PendingApprovalThreshold
                });
            }
        }

        // Check pending fulfillment tickets
        var pendingFulfillment = await _ticketingApiClient.GetPendingFulfillmentTicketsAsync(cancellationToken);
        foreach (var ticket in pendingFulfillment)
        {
            var age = now - ticket.UpdatedAt;
            if (age > PendingFulfillmentThreshold)
            {
                violations.Add(new SlaViolation
                {
                    TicketId = ticket.TicketId,
                    Title = ticket.Title,
                    Status = ticket.Status,
                    Priority = ticket.Priority,
                    Age = age,
                    Threshold = PendingFulfillmentThreshold
                });
            }
        }

        return violations;
    }

    /// <summary>
    /// Generates basic alerts when LLM is unavailable.
    /// </summary>
    private static List<OperationsAlert> GenerateFallbackAlerts(HealthScanResult scanResult)
    {
        var alerts = new List<OperationsAlert>();

        foreach (var service in scanResult.Services.Where(s => !s.IsHealthy))
        {
            alerts.Add(new OperationsAlert
            {
                Severity = "Critical",
                Title = $"{service.ServiceName} is down",
                Description = $"Health check failed for {service.ServiceName} at {service.Endpoint}: {service.Error}",
                Remediation = $"Check {service.ServiceName} logs and restart if necessary"
            });
        }

        foreach (var dlq in scanResult.DeadLetterQueues.Where(d => d.MessageCount > 0))
        {
            alerts.Add(new OperationsAlert
            {
                Severity = dlq.MessageCount > 10 ? "Critical" : "Warning",
                Title = $"DLQ messages in {dlq.SubscriptionName}",
                Description = $"{dlq.MessageCount} messages in dead letter queue for {dlq.SubscriptionName}",
                Remediation = "Review dead letter queue messages and reprocess or discard"
            });
        }

        foreach (var violation in scanResult.SlaViolations)
        {
            alerts.Add(new OperationsAlert
            {
                Severity = violation.Priority == "Critical" ? "Critical" : "Warning",
                Title = $"SLA violation: {violation.TicketId}",
                Description = $"Ticket {violation.TicketId} ({violation.Title}) has been in {violation.Status} for {violation.Age.TotalMinutes:F0} minutes (threshold: {violation.Threshold.TotalMinutes:F0} min)",
                Remediation = "Review ticket and take action to progress it"
            });
        }

        return alerts;
    }
}
