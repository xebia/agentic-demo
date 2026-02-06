namespace Ticketing.Messaging.Abstractions;

/// <summary>
/// Publishes ticket events to a messaging infrastructure.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(TicketEvent ticketEvent, CancellationToken cancellationToken = default);
}
