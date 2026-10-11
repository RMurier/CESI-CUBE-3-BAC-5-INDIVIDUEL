using System.Collections.Concurrent;
using Collector.Messaging;

namespace Collector.Testing;

/// <summary>
/// Remplace <see cref="IEventBus"/> dans les tests d'intégration : aucun RabbitMQ réel requis,
/// les événements publiés sont capturés pour assertion (<see cref="Published"/>).
/// </summary>
public sealed class FakeEventBus : IEventBus
{
    private readonly ConcurrentQueue<object> _published = new();

    public IReadOnlyCollection<object> Published => _published.ToArray();

    public Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        _published.Enqueue(message);
        return Task.CompletedTask;
    }

    public IEnumerable<TEvent> PublishedOf<TEvent>() => Published.OfType<TEvent>();
}
