namespace Ticketing.Messaging.Abstractions;

/// <summary>
/// Flat DTO carrying ticket data within a TicketEvent envelope.
/// </summary>
public class TicketEventPayload
{
    public required string TicketId { get; set; }
    public required string Title { get; set; }
    public string? Status { get; set; }
    public string? AssignedQueue { get; set; }
    public string? TriageDecision { get; set; }
    public string? TriageNotes { get; set; }
    public string? Priority { get; set; }
    public string? Category { get; set; }
    public string? CreatedBy { get; set; }
    public string? ChangedBy { get; set; }
    public int AttemptCount { get; set; }
}
