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
    public const string TicketApprovalRequired = "ticket.approval-required";
    public const string TicketApproved = "ticket.approved";
    public const string TicketRejected = "ticket.rejected";
    public const string TicketFulfillmentRequested = "ticket.fulfillment-requested";
    public const string TicketOrderSubmitted = "ticket.order-submitted";
    public const string TicketFulfilled = "ticket.fulfilled";
}
