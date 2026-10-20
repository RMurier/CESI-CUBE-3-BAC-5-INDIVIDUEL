using System.Text.Json;
using Collector.Contracts.Events;
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

    private static BasicDeliverEventArgs Delivery(ArticlePublishedEvent message, ulong deliveryTag = 1) => new(
        consumerTag: "test-consumer",
        deliveryTag: deliveryTag,
        redelivered: false,
        exchange: "collector.events",
        routingKey: nameof(ArticlePublishedEvent),
        properties: new BasicProperties(),
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
