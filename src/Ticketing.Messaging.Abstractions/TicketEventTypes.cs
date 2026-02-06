namespace Ticketing.Messaging.Abstractions;

/// <summary>
/// Constants for ticket event types used as message subjects/routing keys.
/// </summary>
public static class TicketEventTypes
{
    public const string TicketCreated = "ticket.created";
    public const string TicketAssigned = "ticket.assigned";
    public const string TicketUpdated = "ticket.updated";
    public const string TicketClosed = "ticket.closed";
}
