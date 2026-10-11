namespace Collector.Messaging;

/// <summary>Abstraction de publication d'événements métier sur l'exchange Collector.</summary>
public interface IEventBus
{
    /// <summary>
    /// Publie <paramref name="message"/> avec comme routing key le nom du type (ex: "ArticleSubmittedEvent").
    /// </summary>
    Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : class;
}
