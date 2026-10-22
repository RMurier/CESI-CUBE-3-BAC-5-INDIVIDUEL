using System.Text;
using System.Text.Json;
using Collector.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Collector.Messaging;

/// <summary>
/// Base générique pour consommer un type d'événement Collector depuis une queue dédiée au service.
/// Chaque service hérite de cette classe par type d'événement qu'il écoute (ex: ModerationResultConsumer).
/// </summary>
/// <typeparam name="TEvent">Type de l'événement (le nom du type = routing key côté publisher).</typeparam>
public abstract class RabbitMqConsumer<TEvent> : BackgroundService
    where TEvent : class
{
    private readonly RabbitMqOptions _options;
    private readonly string _queueName;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    protected RabbitMqConsumer(
        IOptions<RabbitMqOptions> options,
        string queueName,
        IServiceScopeFactory scopeFactory,
        ILogger logger)
    {
        _options = options.Value;
        _queueName = queueName;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Traite un événement désérialisé. Lever une exception = message renvoyé en nack (requeue).</summary>
    protected abstract Task HandleAsync(TEvent message, IServiceProvider scopedServices, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            VirtualHost = _options.VirtualHost,
            UserName = _options.UserName,
            Password = _options.Password,
        };

        var routingKey = typeof(TEvent).Name;

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(_queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(_queueName, _options.ExchangeName, routingKey, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, prefetchCount: 10, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleDeliveryAsync(channel, ea, routingKey, stoppingToken);

        await channel.BasicConsumeAsync(_queueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        // Garde le BackgroundService vivant tant que le service n'est pas arrêté.
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    /// <summary>
    /// Traite une livraison : désérialise, délègue à <see cref="HandleAsync"/>, ack/nack. Extrait de
    /// <see cref="ExecuteAsync"/> pour être testable sans connexion RabbitMQ réelle (voir RabbitMqConsumerTests).
    /// </summary>
    internal async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs ea, string routingKey, CancellationToken cancellationToken)
    {
        // Propagé par RabbitMqEventBus.PublishAsync depuis la requête HTTP d'origine (si elle existe) :
        // relie une erreur de consumer, asynchrone et découplée dans le temps, à la transaction qui
        // l'a causée, au lieu de devoir recouper des logs à la main entre services.
        var correlationId = ea.BasicProperties.CorrelationId;

        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            var message = JsonSerializer.Deserialize<TEvent>(json);
            if (message is not null)
            {
                using var scope = _scopeFactory.CreateScope();
                await HandleAsync(message, scope.ServiceProvider, cancellationToken);
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du traitement de l'événement {EventType} sur la queue {Queue} (CorrelationId: {CorrelationId})", routingKey, _queueName, correlationId);

            // Jusqu'ici invisible ailleurs que dans kubectl logs : un consumer tourne dans un
            // BackgroundService, hors de toute requête HTTP, donc jamais vu par UseOmniaExceptionLogging.
            using var omniaScope = _scopeFactory.CreateScope();
            var omnia = omniaScope.ServiceProvider.GetService<IOmniaClient>();
            if (omnia is not null)
            {
                await omnia.SendLogAsync(
                    category: "ConsumerError",
                    level: "error",
                    message: ex.Message,
                    payload: new
                    {
                        service = omnia.ServiceName,
                        correlationId,
                        eventType = routingKey,
                        queue = _queueName,
                        details = ex.ToString(),
                    },
                    ct: cancellationToken);
            }

            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: cancellationToken);
        }
    }
}
