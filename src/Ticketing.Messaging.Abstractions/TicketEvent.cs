namespace Ticketing.Messaging.Abstractions;

/// <summary>
/// Event envelope for ticket-related messages.
/// </summary>
public class TicketEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public required string EventType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? CorrelationId { get; set; }
    public required TicketEventPayload Payload { get; set; }
}
