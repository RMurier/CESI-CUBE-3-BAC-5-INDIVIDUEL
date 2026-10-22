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

        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        _consumer = new TestConsumer(Options.Create(new RabbitMqOptions()), scopeFactory, NullLogger.Instance);
    }

    public void Dispose() => _consumer.Dispose();

    private static BasicDeliverEventArgs Delivery(ArticlePublishedEvent message, ulong deliveryTag = 1, string? correlationId = null) => new(
        consumerTag: "test-consumer",
        deliveryTag: deliveryTag,
        redelivered: false,
        exchange: "collector.events",
        routingKey: nameof(ArticlePublishedEvent),
        properties: new BasicProperties { CorrelationId = correlationId },
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
    public async Task HandleDeliveryAsync_nacks_with_requeue_when_HandleAsync_throws()
    {
        _consumer.ThrowOnHandle = true;
        var evt = new ArticlePublishedEvent(Guid.NewGuid(), Guid.NewGuid(), "x", 1m, DateTimeOffset.UtcNow);

        await _consumer.HandleDeliveryAsync(_channel.Object, Delivery(evt, deliveryTag: 3), nameof(ArticlePublishedEvent), CancellationToken.None);

        Assert.Empty(_consumer.Handled);
        _channel.Verify(c => c.BasicNackAsync(3UL, false, true, It.IsAny<CancellationToken>()), Times.Once);
        _channel.Verify(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
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
        // n'y est pas enregistré. Vérifie que le nack a bien lieu malgré l'absence du client.
        _consumer.ThrowOnHandle = true;
        var evt = new ArticlePublishedEvent(Guid.NewGuid(), Guid.NewGuid(), "x", 1m, DateTimeOffset.UtcNow);

        await _consumer.HandleDeliveryAsync(_channel.Object, Delivery(evt, deliveryTag: 9), nameof(ArticlePublishedEvent), CancellationToken.None);

        _channel.Verify(c => c.BasicNackAsync(9UL, false, true, It.IsAny<CancellationToken>()), Times.Once);
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
