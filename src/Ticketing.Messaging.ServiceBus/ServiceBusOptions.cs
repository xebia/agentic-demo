namespace Ticketing.Messaging.ServiceBus;

/// <summary>
/// Configuration options for Azure Service Bus connectivity.
/// Binds to the "ServiceBus" configuration section.
/// </summary>
public class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    /// <summary>
    /// Azure Service Bus connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Topic name for ticket events. Defaults to "tickets.events".
    /// </summary>
    public string TopicName { get; set; } = "tickets.events";
}
