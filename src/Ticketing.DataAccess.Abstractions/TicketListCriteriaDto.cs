namespace Ticketing.DataAccess.Abstractions;

/// <summary>
/// Criteria DTO for filtering ticket lists at the data access layer.
/// Uses primitive types to avoid coupling to domain enums.
/// </summary>
public record TicketListCriteriaDto
{
    /// <summary>
    /// Filter by status values (e.g., "new", "in-progress", "closed").
    /// </summary>
    public string[]? Statuses { get; init; }
    
    /// <summary>
    /// Filter by assigned queue (e.g., "helpdesk", "purchasing").
    /// </summary>
    public string? AssignedQueue { get; init; }
    
    /// <summary>
    /// Filter by assigned user.
    /// </summary>
    public string? AssignedTo { get; init; }
    
    /// <summary>
    /// Filter by creator.
    /// </summary>
    public string? CreatedBy { get; init; }
    
    /// <summary>
    /// Filter by parent ticket ID.
    /// </summary>
    public string? ParentTicketId { get; init; }
    
    /// <summary>
    /// Filter by ticket type (e.g., "support", "purchase").
    /// </summary>
    public string? TicketType { get; init; }
    
    /// <summary>
    /// Include child tickets in results.
    /// </summary>
    public bool IncludeChildren { get; init; }
    
    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int Limit { get; init; } = 50;
    
    /// <summary>
    /// Number of results to skip (for pagination).
    /// </summary>
    public int Offset { get; init; } = 0;
}
