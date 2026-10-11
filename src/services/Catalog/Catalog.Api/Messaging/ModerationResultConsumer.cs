using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Collector.Contracts.Events;
using Collector.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Catalog.Api.Messaging;

/// <summary>
/// Consomme le verdict du service Moderation et fait passer l'article en ligne (ou rejeté).
/// Publie ensuite ArticlePublishedEvent pour que Notification avertisse les acheteurs intéressés.
/// </summary>
public sealed class ModerationResultConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<ModerationResultConsumer> logger)
    : RabbitMqConsumer<ArticleModerationResultEvent>(options, "catalog.article-moderation-result", scopeFactory, logger)
{
    protected override async Task HandleAsync(
        ArticleModerationResultEvent message,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var db = scopedServices.GetRequiredService<CatalogDbContext>();
        var eventBus = scopedServices.GetRequiredService<IEventBus>();

        var article = await db.Articles.SingleOrDefaultAsync(a => a.Id == message.ArticleId, cancellationToken);
        if (article is null)
        {
            return;
        }

        if (message.Approved)
        {
            article.Status = ArticleStatus.Published;
            article.PublishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            await eventBus.PublishAsync(
                new ArticlePublishedEvent(article.Id, article.ShopId, article.Title, article.Price, article.PublishedAt.Value),
                cancellationToken);
        }
        else
        {
            article.Status = ArticleStatus.Rejected;
            article.RejectionReason = message.RejectionReason;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
