using System.Text;
using System.Text.Json;
using Collector.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Collector.Messaging;

/// <summary>
/// Implémentation RabbitMQ de <see cref="IEventBus"/>. Une connexion/canal est créé à la demande et
/// réutilisé pour le reste de la durée de vie du processus (singleton).
/// </summary>
public sealed class RabbitMqEventBus : IEventBus, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqEventBus(IOptions<RabbitMqOptions> options, IHttpContextAccessor httpContextAccessor)
    {
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>Seam de test : évite la vraie connexion RabbitMQ (voir RabbitMqEventBusTests).</summary>
    internal RabbitMqEventBus(IOptions<RabbitMqOptions> options, IChannel testChannel, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options.Value;
        _channel = testChannel;
        _httpContextAccessor = httpContextAccessor ?? new HttpContextAccessor();
    }

    public async Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        var channel = await GetChannelAsync(cancellationToken);
        var routingKey = typeof(TEvent).Name;
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            // Permet de retrouver, dans le consumer comme dans Omnia, la requête HTTP d'origine qui
            // a déclenché cet événement — sans ça, une erreur de consumer est impossible à relier à
            // la transaction qui l'a causée une fois publiée de manière asynchrone.
            CorrelationId = CorrelationId.Get(_httpContextAccessor),
        };

        await channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    internal async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                VirtualHost = _options.VirtualHost,
                UserName = _options.UserName,
                Password = _options.Password,
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await _channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                cancellationToken: cancellationToken);

            return _channel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
