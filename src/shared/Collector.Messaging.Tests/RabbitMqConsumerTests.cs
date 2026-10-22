using System.Text.Json;
using Collector.Contracts.Events;
using Collector.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace Collector.Messaging.Tests;

/// <summary>
/// RabbitMqConsumer&lt;TEvent&gt; n'est exercé par aucun test d'intégration (ils retirent tous les
/// IHostedService, voir SqlServerWebApplicationFactory) : HandleDeliveryAsync (extrait de ExecuteAsync)
/// est donc la seule couverture réelle du cycle désérialisation / ack / nack-requeue.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RabbitMqConsumerTests : IDisposable
{
    private readonly Mock<IChannel> _channel = new();
    private readonly TestConsumer _consumer;

    public RabbitMqConsumerTests()
    {
        _channel.Setup(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        _channel.Setup(c => c.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        _channel.Setup(c => c.BasicPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        _consumer = new TestConsumer(Options.Create(new RabbitMqOptions()), scopeFactory, NullLogger.Instance);
    }

    public void Dispose() => _consumer.Dispose();

    private static BasicDeliverEventArgs Delivery(ArticlePublishedEvent message, ulong deliveryTag = 1, string? correlationId = null, int attempt = 0) => new(
        consumerTag: "test-consumer",
        deliveryTag: deliveryTag,
        redelivered: false,
        exchange: "collector.events",
        routingKey: nameof(ArticlePublishedEvent),
        properties: new BasicProperties
        {
            CorrelationId = correlationId,
            Headers = attempt > 0 ? new Dictionary<string, object?> { ["x-collector-retry-count"] = attempt } : null,
        },
        body: JsonSerializer.SerializeToUtf8Bytes(message),
        cancellationToken: CancellationToken.None);

    [Fact]
    public async Task HandleDeliveryAsync_deserializes_delegates_to_HandleAsync_then_acks()
    {
        var evt = new ArticlePublishedEvent(Guid.NewGuid(), Guid.NewGuid(), "Vélo vintage", 42m, DateTimeOffset.UtcNow);

        await _consumer.HandleDeliveryAsync(_channel.Object, Delivery(evt, deliveryTag: 7), nameof(ArticlePublishedEvent), CancellationToken.None);

        Assert.Equal(evt, Assert.Single(_consumer.Handled));
        _channel.Verify(c => c.BasicAckAsync(7UL, false, It.IsAny<CancellationToken>()), Times.Once);
        _channel.Verify(c => c.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleDeliveryAsync_republishes_with_an_incremented_retry_count_when_HandleAsync_throws()
    {
        // BasicNackAsync(requeue: true) redistribue le message sans pouvoir toucher à ses headers :
        // on republie nous-mêmes une copie avec le compteur de tentatives incrémenté, puis on ack
        // l'originale (jamais de BasicNackAsync, sinon RabbitMQ la redistribuerait EN PLUS).
        _consumer.ThrowOnHandle = true;
        BasicProperties? republishedProps = null;
        _channel
            .Setup(c => c.BasicPublishAsync(
                string.Empty, "test-queue", false,
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>(
                (_, _, _, props, _, _) => republishedProps = props)
            .Returns(ValueTask.CompletedTask);
        var evt = new ArticlePublishedEvent(Guid.NewGuid(), Guid.NewGuid(), "x", 1m, DateTimeOffset.UtcNow);

        await _consumer.HandleDeliveryAsync(_channel.Object, Delivery(evt, deliveryTag: 3, attempt: 1), nameof(ArticlePublishedEvent), CancellationToken.None);

        Assert.Empty(_consumer.Handled);
        Assert.Equal(2, republishedProps!.Headers!["x-collector-retry-count"]);
        _channel.Verify(c => c.BasicAckAsync(3UL, false, It.IsAny<CancellationToken>()), Times.Once);
        _channel.Verify(c => c.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleDeliveryAsync_abandons_the_message_after_MaxAttempts_instead_of_looping_forever()
    {
        var omnia = new Mock<IOmniaClient>();
        omnia.SetupGet(o => o.ServiceName).Returns("Moderation");
        string? capturedCategory = null;
        omnia.Setup(o => o.SendLogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, object?, CancellationToken>((category, _, _, _, _) => capturedCategory = category)
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(omnia.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        using var consumer = new TestConsumer(Options.Create(new RabbitMqOptions()), scopeFactory, NullLogger.Instance) { ThrowOnHandle = true };

        var evt = new ArticlePublishedEvent(Guid.NewGuid(), Guid.NewGuid(), "x", 1m, DateTimeOffset.UtcNow);
        // attempt: 4 -> 5e tentative -> atteint MaxAttempts (5) -> abandon, pas de republish.
        await consumer.HandleDeliveryAsync(_channel.Object, Delivery(evt, deliveryTag: 11, attempt: 4), nameof(ArticlePublishedEvent), CancellationToken.None);

        Assert.Equal("ConsumerMessageAbandoned", capturedCategory);
        _channel.Verify(c => c.BasicAckAsync(11UL, false, It.IsAny<CancellationToken>()), Times.Once);
        _channel.Verify(c => c.BasicPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleDeliveryAsync_reports_consumer_errors_to_Omnia_with_the_correlation_id()
    {
        var omnia = new Mock<IOmniaClient>();
        omnia.SetupGet(o => o.ServiceName).Returns("Catalog");
        object? capturedPayload = null;
        omnia.Setup(o => o.SendLogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, object?, CancellationToken>((_, _, _, payload, _) => capturedPayload = payload)
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(omnia.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        using var consumer = new TestConsumer(Options.Create(new RabbitMqOptions()), scopeFactory, NullLogger.Instance) { ThrowOnHandle = true };

        var evt = new ArticlePublishedEvent(Guid.NewGuid(), Guid.NewGuid(), "x", 1m, DateTimeOffset.UtcNow);
        await consumer.HandleDeliveryAsync(_channel.Object, Delivery(evt, correlationId: "corr-42"), nameof(ArticlePublishedEvent), CancellationToken.None);

        omnia.Verify(o => o.SendLogAsync("ConsumerError", "error", "boom", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(capturedPayload);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(capturedPayload));
        Assert.Equal("Catalog", json.RootElement.GetProperty("service").GetString());
        Assert.Equal("corr-42", json.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal(nameof(ArticlePublishedEvent), json.RootElement.GetProperty("eventType").GetString());
    }

    [Fact]
    public async Task HandleDeliveryAsync_does_not_throw_when_Omnia_is_not_registered()
    {
        // _consumer (champ partagé) est construit avec un IServiceScopeFactory vide : IOmniaClient
        // n'y est pas enregistré. Vérifie que la republication a bien lieu malgré l'absence du client.
        _consumer.ThrowOnHandle = true;
        var evt = new ArticlePublishedEvent(Guid.NewGuid(), Guid.NewGuid(), "x", 1m, DateTimeOffset.UtcNow);

        await _consumer.HandleDeliveryAsync(_channel.Object, Delivery(evt, deliveryTag: 9), nameof(ArticlePublishedEvent), CancellationToken.None);

        _channel.Verify(c => c.BasicAckAsync(9UL, false, It.IsAny<CancellationToken>()), Times.Once);
        _channel.Verify(c => c.BasicPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class TestConsumer(IOptions<RabbitMqOptions> options, IServiceScopeFactory scopeFactory, Microsoft.Extensions.Logging.ILogger logger)
        : RabbitMqConsumer<ArticlePublishedEvent>(options, "test-queue", scopeFactory, logger)
    {
        public List<ArticlePublishedEvent> Handled { get; } = [];
        public bool ThrowOnHandle { get; set; }

        protected override Task HandleAsync(ArticlePublishedEvent message, IServiceProvider scopedServices, CancellationToken cancellationToken)
        {
            if (ThrowOnHandle) throw new InvalidOperationException("boom");
            Handled.Add(message);
            return Task.CompletedTask;
        }
    }
}
