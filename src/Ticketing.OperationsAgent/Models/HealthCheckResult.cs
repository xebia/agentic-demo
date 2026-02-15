namespace Ticketing.OperationsAgent.Models;

public class HealthScanResult
{
    public List<ServiceHealthStatus> Services { get; set; } = [];
    public List<DlqStatus> DeadLetterQueues { get; set; } = [];
    public List<SlaViolation> SlaViolations { get; set; } = [];
    public DateTime ScanTime { get; set; } = DateTime.UtcNow;
}

public class ServiceHealthStatus
{
    public required string ServiceName { get; set; }
    public required string Endpoint { get; set; }
    public bool IsHealthy { get; set; }
    public string? Status { get; set; }
    public string? Error { get; set; }
    public int ResponseTimeMs { get; set; }
}

public class DlqStatus
{
    public required string SubscriptionName { get; set; }
    public long MessageCount { get; set; }
}

public class SlaViolation
{
    public required string TicketId { get; set; }
    public required string Title { get; set; }
    public required string Status { get; set; }
    public required string Priority { get; set; }
    public TimeSpan Age { get; set; }
    public TimeSpan Threshold { get; set; }
}
