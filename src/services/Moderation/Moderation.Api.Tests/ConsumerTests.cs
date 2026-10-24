using Collector.Contracts.Events;
using Collector.Messaging;
using Collector.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moderation.Api.Infrastructure;
using Moderation.Api.Messaging;
using Xunit;

namespace Moderation.Api.Tests;

[Trait("Category", "Integration")]
public sealed class ConsumerTests(ModerationApiFactory factory) : IClassFixture<ModerationApiFactory>
{
    private static ArticleSubmittedEvent Submitted(Guid articleId, decimal price, string description) =>
        new(articleId, Guid.NewGuid(), Guid.NewGuid(), "Figurine", description, price, 5m, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Submitted_article_is_approved_persisted_and_result_published()
    {
        var consumer = new ArticleSubmittedConsumer(
            Options.Create(new RabbitMqOptions()), factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ArticleSubmittedConsumer>.Instance);
        var articleId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        await ConsumerTestHelper.HandleAsync(consumer, Submitted(articleId, 20m, "Une belle description"), scope.ServiceProvider);

        var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();
        var decision = await db.Decisions.SingleAsync(d => d.ArticleId == articleId);
        Assert.True(decision.Approved);
        Assert.Contains(factory.EventBus.PublishedOf<ArticleModerationResultEvent>(), e => e.ArticleId == articleId && e.Approved);
    }

    [Fact]
    public async Task Submitted_article_with_invalid_price_is_rejected()
    {
        var consumer = new ArticleSubmittedConsumer(
            Options.Create(new RabbitMqOptions()), factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ArticleSubmittedConsumer>.Instance);
        var articleId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        await ConsumerTestHelper.HandleAsync(consumer, Submitted(articleId, 0m, "Une belle description"), scope.ServiceProvider);

        var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();
        var decision = await db.Decisions.SingleAsync(d => d.ArticleId == articleId);
        Assert.False(decision.Approved);
        Assert.NotNull(decision.RejectionReason);
    }

    [Theory]
    [InlineData(100, 110, true)]
    [InlineData(100, 300, false)]
    [InlineData(0, 10, false)]
    public async Task Price_change_is_flagged_when_variation_exceeds_50_percent(decimal previous, decimal current, bool approved)
    {
        var consumer = new PriceChangedConsumer(
            Options.Create(new RabbitMqOptions()), factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PriceChangedConsumer>.Instance);
        var articleId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        await ConsumerTestHelper.HandleAsync(consumer, new PriceChangedEvent(articleId, "Article de test", previous, current, DateTimeOffset.UtcNow), scope.ServiceProvider);

        var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();
        var decision = await db.Decisions.SingleAsync(d => d.ArticleId == articleId);
        Assert.Equal(approved, decision.Approved);
        Assert.Equal(approved, decision.RejectionReason is null);
    }
}
