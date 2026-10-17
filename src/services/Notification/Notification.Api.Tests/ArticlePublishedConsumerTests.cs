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
public sealed class ArticlePublishedConsumerTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Fact]
    public async Task Matching_subscription_gets_a_notification_and_others_do_not()
    {
        var matchingUser = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        db.Subscriptions.AddRange(
            new InterestSubscription { UserId = matchingUser, Keyword = "figurine" },
            new InterestSubscription { UserId = otherUser, Keyword = "cassette" });
        await db.SaveChangesAsync();

        var consumer = new ArticlePublishedConsumer(
            Options.Create(new RabbitMqOptions()), factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ArticlePublishedConsumer>.Instance);
        var published = new ArticlePublishedEvent(Guid.NewGuid(), Guid.NewGuid(), "Figurine Mario rare", 42m, DateTimeOffset.UtcNow);

        await ConsumerTestHelper.HandleAsync(consumer, published, scope.ServiceProvider);

        Assert.True(await db.Notifications.AnyAsync(n => n.UserId == matchingUser));
        Assert.False(await db.Notifications.AnyAsync(n => n.UserId == otherUser));
    }
}
