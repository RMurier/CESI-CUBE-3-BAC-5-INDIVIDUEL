using Collector.Contracts.Events;
using Collector.Messaging;
using Collector.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Notification.Api.Infrastructure;
using Notification.Api.Messaging;
using Xunit;

namespace Notification.Api.Tests;

[Trait("Category", "Integration")]
public sealed class OrderPaidConsumerTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Fact]
    public async Task A_paid_order_notifies_both_the_buyer_and_the_seller()
    {
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        var consumer = new OrderPaidConsumer(
            Options.Create(new RabbitMqOptions()), factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OrderPaidConsumer>.Instance);
        var paid = new OrderPaidEvent(
            Guid.NewGuid(), Guid.NewGuid(), "Poster dédicacé", buyerId, sellerId, 110m, 5.50m, DateTimeOffset.UtcNow);

        await ConsumerTestHelper.HandleAsync(consumer, paid, scope.ServiceProvider);

        Assert.Contains("Paiement confirmé", (await db.Notifications.SingleAsync(n => n.UserId == buyerId)).Message);
        Assert.Contains("vendu", (await db.Notifications.SingleAsync(n => n.UserId == sellerId)).Message);
    }
}
