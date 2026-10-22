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
    // Un message qui échoue systématiquement (ex: violation de contrainte en base) ne réussira
    // jamais, peu importe le nombre de tentatives : sans plafond, BasicNackAsync(requeue: true)
    // le redistribue aussitôt, en boucle infinie, qui consomme du CPU et spamme Omnia sans fin.
    private const int MaxAttempts = 5;
    private const string RetryCountHeader = "x-collector-retry-count";

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
        var attempt = GetAttempt(ea.BasicProperties);

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
            var rootMessage = ExceptionDetails.RootMessage(ex);
            var abandoning = attempt + 1 >= MaxAttempts;

            _logger.LogError(ex, "Échec du traitement de l'événement {EventType} sur la queue {Queue} (CorrelationId: {CorrelationId}, tentative {Attempt}/{MaxAttempts}){Abandon}",
                routingKey, _queueName, correlationId, attempt + 1, MaxAttempts, abandoning ? " — ABANDONNÉ" : "");

            // Jusqu'ici invisible ailleurs que dans kubectl logs : un consumer tourne dans un
            // BackgroundService, hors de toute requête HTTP, donc jamais vu par UseOmniaExceptionLogging.
            using var omniaScope = _scopeFactory.CreateScope();
            var omnia = omniaScope.ServiceProvider.GetService<IOmniaClient>();
            if (omnia is not null)
            {
                await omnia.SendLogAsync(
                    // Catégorie distincte à l'abandon : un pic de ConsumerError peut être transitoire
                    // (ex: base temporairement injoignable), un ConsumerMessageAbandoned signale un
                    // message qui ne passera jamais et qu'il faut regarder manuellement.
                    category: abandoning ? "ConsumerMessageAbandoned" : "ConsumerError",
                    level: "error",
                    // Message de la cause racine, pas celui du wrapper externe (EF Core, par ex.,
                    // enrobe systématiquement avec un message générique inutile sans l'InnerException).
                    message: rootMessage,
                    payload: new
                    {
                        service = omnia.ServiceName,
                        correlationId,
                        eventType = routingKey,
                        queue = _queueName,
                        attempt = attempt + 1,
                        maxAttempts = MaxAttempts,
                        outerMessage = ex.Message,
                        details = ex.ToString(),
                    },
                    ct: cancellationToken);
            }

            if (abandoning)
            {
                // Plus de tentative : on retire le message de la queue sans le retraiter, sinon il
                // boucle à l'infini sur une erreur qui ne se résoudra jamais seule.
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
            }
            else
            {
                // BasicNackAsync(requeue: true) redistribue le même message sans pouvoir modifier ses
                // headers : on republie nous-mêmes une copie avec le compteur incrémenté, puis on ack
                // l'originale (sinon RabbitMQ la redistribue EN PLUS de notre copie).
                var retryProps = new BasicProperties(ea.BasicProperties)
                {
                    Headers = new Dictionary<string, object?>(ea.BasicProperties.Headers ?? new Dictionary<string, object?>())
                    {
                        [RetryCountHeader] = attempt + 1,
                    },
                };
                await channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: _queueName,
                    mandatory: false,
                    basicProperties: retryProps,
                    body: ea.Body,
                    cancellationToken: cancellationToken);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
            }
        }
    }

    private static int GetAttempt(IReadOnlyBasicProperties props) => props.Headers is { } headers
        && headers.TryGetValue(RetryCountHeader, out var raw)
        ? raw switch { int i => i, long l => (int)l, _ => 0 }
        : 0;
}
