using Collector.Contracts.Events;
using Collector.Messaging;
using Collector.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Notification.Api.Domain;
using Notification.Api.Infrastructure;
using Notification.Api.Messaging;
using Xunit;

namespace Notification.Api.Tests;

[Trait("Category", "Integration")]
public sealed class PriceChangedConsumerTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Fact]
    public async Task Matching_subscription_gets_a_notification_and_others_do_not()
    {
        Guid matchingUser = Guid.NewGuid();
        Guid otherUser = Guid.NewGuid();
        using IServiceScope scope = factory.Services.CreateScope();
        NotificationDbContext db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        db.Subscriptions.AddRange(
            new InterestSubscription { UserId = matchingUser, Keyword = "figurine" },
            new InterestSubscription { UserId = otherUser, Keyword = "cassette" });
        await db.SaveChangesAsync();

        PriceChangedConsumer consumer = new(
            Options.Create(new RabbitMqOptions()), factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PriceChangedConsumer>.Instance);
        PriceChangedEvent changed = new(Guid.NewGuid(), "Figurine Mario rare", 42m, 30m, DateTimeOffset.UtcNow);

        await ConsumerTestHelper.HandleAsync(consumer, changed, scope.ServiceProvider);

        Assert.True(await db.Notifications.AnyAsync(n => n.UserId == matchingUser));
        Assert.False(await db.Notifications.AnyAsync(n => n.UserId == otherUser));
    }

    [Fact]
    public async Task Message_mentions_whether_the_price_went_up_or_down()
    {
        Guid matchingUser = Guid.NewGuid();
        using IServiceScope scope = factory.Services.CreateScope();
        NotificationDbContext db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        db.Subscriptions.Add(new InterestSubscription { UserId = matchingUser, Keyword = "baskets" });
        await db.SaveChangesAsync();

        PriceChangedConsumer consumer = new(
            Options.Create(new RabbitMqOptions()), factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PriceChangedConsumer>.Instance);
        PriceChangedEvent changed = new(Guid.NewGuid(), "Baskets collector", 100m, 150m, DateTimeOffset.UtcNow);

        await ConsumerTestHelper.HandleAsync(consumer, changed, scope.ServiceProvider);

        UserNotification notification = await db.Notifications.SingleAsync(n => n.UserId == matchingUser);
        Assert.Contains("augmenté", notification.Message);
    }
}
