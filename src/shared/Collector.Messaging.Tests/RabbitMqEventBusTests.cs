using System.Text.Json;
using Collector.Contracts.Events;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace Collector.Messaging.Tests;

/// <summary>
/// RabbitMqEventBus n'est exercé par aucun test d'intégration (ils remplacent IEventBus par
/// FakeEventBus, voir SqlServerWebApplicationFactory) : cette classe est donc la seule couverture
/// réelle de la publication RabbitMQ. IChannel/IConnection sont des interfaces, mockables sans broker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RabbitMqEventBusTests : IDisposable
{
    private readonly Mock<IChannel> _channel = new();
    private readonly RabbitMqEventBus _bus;

    public RabbitMqEventBusTests()
    {
        _channel.Setup(c => c.IsOpen).Returns(true);
        _bus = new RabbitMqEventBus(
            Options.Create(new RabbitMqOptions { ExchangeName = "collector.events" }),
            _channel.Object);
    }

    public void Dispose() => _bus.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [Fact]
    public async Task PublishAsync_routes_by_the_event_type_name_to_the_configured_exchange()
    {
        string? capturedExchange = null;
        string? capturedRoutingKey = null;
        BasicProperties? capturedProps = null;
        var capturedBody = ReadOnlyMemory<byte>.Empty;

        _channel
            .Setup(c => c.BasicPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>(
                (exchange, routingKey, _, props, body, _) =>
                {
                    capturedExchange = exchange;
                    capturedRoutingKey = routingKey;
                    capturedProps = props;
                    capturedBody = body;
                })
            .Returns(ValueTask.CompletedTask);

        var evt = new ArticlePublishedEvent(Guid.NewGuid(), Guid.NewGuid(), "Vélo vintage", 42m, DateTimeOffset.UtcNow);
        await _bus.PublishAsync(evt);

        Assert.Equal("collector.events", capturedExchange);
        Assert.Equal(nameof(ArticlePublishedEvent), capturedRoutingKey);
        Assert.True(capturedProps!.Persistent);
        Assert.Equal("application/json", capturedProps.ContentType);
        Assert.Equal(evt, JsonSerializer.Deserialize<ArticlePublishedEvent>(capturedBody.Span));
    }

    [Fact]
    public async Task PublishAsync_reuses_the_already_open_channel_without_reconnecting()
    {
        _channel
            .Setup(c => c.BasicPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var evt = new PriceChangedEvent(Guid.NewGuid(), 10m, 8m, DateTimeOffset.UtcNow);
        await _bus.PublishAsync(evt);
        await _bus.PublishAsync(evt);

        _channel.Verify(c => c.BasicPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _channel.Verify(c => c.IsOpen, Times.AtLeast(2));
    }
}
