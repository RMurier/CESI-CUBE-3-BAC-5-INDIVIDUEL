using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Catalog.Api.Messaging;
using Collector.Contracts.Events;
using Collector.Messaging;
using Collector.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Catalog.Api.Tests;

[Trait("Category", "Integration")]
public sealed class ModerationResultConsumerTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private ModerationResultConsumer CreateConsumer() => new(
        Options.Create(new RabbitMqOptions()), factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ModerationResultConsumer>.Instance);

    private static async Task<Article> SeedArticleAsync(CatalogDbContext db)
    {
        var shop = new Shop { SellerId = Guid.NewGuid(), Name = "Boutique" };
        var article = new Article { ShopId = shop.Id, Title = "Figurine", Description = "Une belle description", Price = 10m, ShippingCost = 2m };
        db.Shops.Add(shop);
        db.Articles.Add(article);
        await db.SaveChangesAsync();
        return article;
    }

    [Fact]
    public async Task Approved_result_publishes_the_article_and_emits_event()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var article = await SeedArticleAsync(db);

        await ConsumerTestHelper.HandleAsync(
            CreateConsumer(), new ArticleModerationResultEvent(article.Id, true, null, DateTimeOffset.UtcNow), scope.ServiceProvider);

        var reloaded = await db.Articles.AsNoTracking().SingleAsync(a => a.Id == article.Id);
        Assert.Equal(ArticleStatus.Published, reloaded.Status);
        Assert.NotNull(reloaded.PublishedAt);
        Assert.Contains(factory.EventBus.PublishedOf<ArticlePublishedEvent>(), e => e.ArticleId == article.Id);
    }

    [Fact]
    public async Task Rejected_result_marks_the_article_rejected_with_reason()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var article = await SeedArticleAsync(db);

        await ConsumerTestHelper.HandleAsync(
            CreateConsumer(), new ArticleModerationResultEvent(article.Id, false, "Prix invalide.", DateTimeOffset.UtcNow), scope.ServiceProvider);

        var reloaded = await db.Articles.AsNoTracking().SingleAsync(a => a.Id == article.Id);
        Assert.Equal(ArticleStatus.Rejected, reloaded.Status);
        Assert.Equal("Prix invalide.", reloaded.RejectionReason);
    }

    [Fact]
    public async Task Result_for_unknown_article_is_ignored()
    {
        using var scope = factory.Services.CreateScope();

        await ConsumerTestHelper.HandleAsync(
            CreateConsumer(), new ArticleModerationResultEvent(Guid.NewGuid(), true, null, DateTimeOffset.UtcNow), scope.ServiceProvider);
    }
}
