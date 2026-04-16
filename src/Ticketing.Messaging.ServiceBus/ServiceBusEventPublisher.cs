using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ticketing.Messaging.Abstractions;
using Ticketing.Messaging.Abstractions.Diagnostics;

namespace Ticketing.Messaging.ServiceBus;

/// <summary>
/// Publishes ticket events to an Azure Service Bus topic.
/// Sets the message Subject to the event type for subscription filtering.
/// </summary>
public class ServiceBusEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly ServiceBusClient _client;
    private readonly ILogger<ServiceBusEventPublisher> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ServiceBusEventPublisher(
        ServiceBusClient client,
        IOptions<ServiceBusOptions> options,
        ILogger<ServiceBusEventPublisher> logger)
    {
        _logger = logger;
        _client = client;
        _sender = _client.CreateSender(options.Value.TopicName);
    }

    public async Task PublishAsync(TicketEvent ticketEvent, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(ticketEvent, JsonOptions);
        var message = new ServiceBusMessage(body)
        {
            Subject = ticketEvent.EventType,
            ContentType = "application/json",
            MessageId = ticketEvent.EventId,
            CorrelationId = ticketEvent.CorrelationId
        };

        try
        {
            await _sender.SendMessageAsync(message, cancellationToken);
            TicketingTelemetry.EventsPublished.Add(1, new KeyValuePair<string, object?>("event_type", ticketEvent.EventType));

            _logger.LogInformation(
                "Published {EventType} event for ticket {TicketId} (MessageId: {MessageId})",
                ticketEvent.EventType,
                ticketEvent.Payload.TicketId,
                ticketEvent.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish {EventType} event for ticket {TicketId}. Payload: {Payload}",
                ticketEvent.EventType, ticketEvent.Payload.TicketId, body);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        // Client is shared via DI — not owned by this instance
        GC.SuppressFinalize(this);
    }
}
