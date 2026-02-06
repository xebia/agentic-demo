using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Messaging.Abstractions;

namespace Ticketing.Messaging.ServiceBus;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Azure Service Bus messaging services.
    /// Binds the "ServiceBus" configuration section and registers IEventPublisher.
    /// </summary>
    public static IServiceCollection AddServiceBusMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ServiceBusOptions>(
            configuration.GetSection(ServiceBusOptions.SectionName));

        services.AddSingleton<IEventPublisher, ServiceBusEventPublisher>();

        return services;
    }
}
