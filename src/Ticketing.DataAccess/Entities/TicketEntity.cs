namespace Ticketing.DataAccess.Entities;

/// <summary>
/// Entity representing a ticket in the ticketing system.
/// </summary>
public class TicketEntity
{
    // Identity
    public string TicketId { get; set; } = string.Empty;

    // Basic Information
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Classification
    public string TicketType { get; set; } = string.Empty; // 'support', 'purchase', 'delivery'
    public string? Category { get; set; } // 'hardware', 'software', 'access', etc.
    public string Priority { get; set; } = "medium"; // 'low', 'medium', 'high', 'critical'

    // Assignment & Routing
    public string Status { get; set; } = "new";
    public string? AssignedQueue { get; set; } // 'helpdesk', 'purchasing', 'fulfillment'
    public string? AssignedTo { get; set; }

    // People
    public string CreatedBy { get; set; } = string.Empty;
    public string? CreatedByName { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    // Workflow
    public string? TriageDecision { get; set; } // 'helpdesk', 'purchasing', 'auto-resolved'
    public string? TriageNotes { get; set; }
    public string? ResolutionNotes { get; set; }

    // Related Tickets
    public string? ParentTicketId { get; set; }

    // Navigation Properties
    public TicketEntity? ParentTicket { get; set; }
    public ICollection<TicketEntity> ChildTickets { get; set; } = [];
    public ICollection<TicketHistoryEntity> History { get; set; } = [];
}
