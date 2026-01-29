namespace Ticketing.DataAccess.Entities;

/// <summary>
/// Entity for tracking ticket field changes for audit trail.
/// </summary>
public class TicketHistoryEntity
{
    public long HistoryId { get; set; }
    public string TicketId { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? ChangeReason { get; set; }

    // Navigation Property
    public TicketEntity Ticket { get; set; } = null!;
}
