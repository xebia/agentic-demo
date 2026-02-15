namespace Ticketing.OperationsAgent.Models;

public class OperationsAlert
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Severity { get; set; } // Critical, Warning, Info
    public required string Title { get; set; }
    public required string Description { get; set; }
    public string? Remediation { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "HealthScan";
}
