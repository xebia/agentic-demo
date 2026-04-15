using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ticketing.Messaging.Abstractions;

namespace Ticketing.Messaging.ServiceBus;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Azure Service Bus messaging services.
    /// Binds the "ServiceBus" configuration section and registers IEventPublisher.
    /// A single shared ServiceBusClient is registered to avoid exhausting
    /// AMQP handle limits on the Service Bus emulator.
    /// </summary>
    public static IServiceCollection AddServiceBusMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ServiceBusOptions>(
            configuration.GetSection(ServiceBusOptions.SectionName));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            return new ServiceBusClient(options.ConnectionString);
        });

        services.AddSingleton<IEventPublisher, ServiceBusEventPublisher>();

        return services;
    }
}
