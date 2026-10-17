using System.Reflection;

namespace Collector.Testing;

/// <summary>
/// Appelle directement le HandleAsync (protégé) d'un RabbitMqConsumer : permet de tester la logique
/// métier d'un consumer sans broker RabbitMQ, avec le vrai DbContext SQL Server de la factory.
/// </summary>
public static class ConsumerTestHelper
{
    public static async Task HandleAsync<TEvent>(object consumer, TEvent message, IServiceProvider scopedServices)
        where TEvent : class
    {
        var method = consumer.GetType().GetMethod("HandleAsync", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException($"HandleAsync introuvable sur {consumer.GetType().Name}.");
        var task = (Task)method.Invoke(consumer, [message, scopedServices, CancellationToken.None])!;
        await task;
    }
}
