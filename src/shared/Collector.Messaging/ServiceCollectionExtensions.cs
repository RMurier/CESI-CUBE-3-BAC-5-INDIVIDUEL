using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Collector.Messaging;

public static class ServiceCollectionExtensions
{
    /// <summary>Enregistre <see cref="IEventBus"/> (RabbitMQ) à partir de la section "RabbitMq" de la config.</summary>
    public static IServiceCollection AddCollectorEventBus(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<IEventBus, RabbitMqEventBus>();
        return services;
    }
}
