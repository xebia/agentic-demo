namespace Ticketing.DataAccess.Abstractions;

/// <summary>
/// DTO for ticket list items (read-only display).
/// </summary>
public record TicketListItemDto(
    string TicketId,
    string Title,
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
    string? ParentTicketId);
