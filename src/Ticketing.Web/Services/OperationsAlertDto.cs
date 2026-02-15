namespace Ticketing.Web.Services;

public class OperationsAlertDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Severity { get; set; } = "Info"; // Critical, Warning, Info
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Remediation { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsAcknowledged { get; set; }
    public string Source { get; set; } = ""; // e.g. "HealthScan", "EventAnomaly"
}
