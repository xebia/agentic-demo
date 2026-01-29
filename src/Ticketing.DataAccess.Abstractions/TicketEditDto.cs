namespace Ticketing.DataAccess.Abstractions;

/// <summary>
/// DTO for ticket edit operations.
/// </summary>
public record TicketEditDto(
    string TicketId,
    string Title,
    string? Description,
    string TicketType,
    string? Category,
    string Priority,
    string Status,
    string? AssignedQueue,
    string? AssignedTo,
    string CreatedBy,
    string? CreatedByName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ClosedAt,
    string? TriageDecision,
    string? TriageNotes,
    string? ResolutionNotes,
    string? ParentTicketId);
