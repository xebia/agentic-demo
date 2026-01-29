namespace Ticketing.DataAccess.Abstractions;

/// <summary>
/// DTO for ticket history entries.
/// </summary>
public record TicketHistoryDto(
    string TicketId,
    string FieldName,
    string? OldValue,
    string? NewValue,
    string ChangedBy,
    DateTime ChangedAt,
    string? ChangeReason);
