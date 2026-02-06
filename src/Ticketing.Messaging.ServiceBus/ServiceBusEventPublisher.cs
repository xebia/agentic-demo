using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ticketing.Messaging.Abstractions;

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
        IOptions<ServiceBusOptions> options,
        ILogger<ServiceBusEventPublisher> logger)
    {
        _logger = logger;
        var config = options.Value;
        _client = new ServiceBusClient(config.ConnectionString);
        _sender = _client.CreateSender(config.TopicName);
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

        await _sender.SendMessageAsync(message, cancellationToken);

        _logger.LogInformation(
            "Published {EventType} event for ticket {TicketId} (MessageId: {MessageId})",
            ticketEvent.EventType,
            ticketEvent.Payload.TicketId,
            ticketEvent.EventId);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
