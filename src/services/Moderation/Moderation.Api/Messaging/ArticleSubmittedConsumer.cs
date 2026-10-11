using Collector.Contracts.Events;
using Collector.Messaging;
using Microsoft.Extensions.Options;
using Moderation.Api.Domain;
using Moderation.Api.Infrastructure;

namespace Moderation.Api.Messaging;

/// <summary>Écoute les articles soumis par Catalog, applique le contrôle automatisé, publie le verdict.</summary>
public sealed class ArticleSubmittedConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<ArticleSubmittedConsumer> logger)
    : RabbitMqConsumer<ArticleSubmittedEvent>(options, "moderation.article-submitted", scopeFactory, logger)
{
    protected override async Task HandleAsync(
        ArticleSubmittedEvent message,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var db = scopedServices.GetRequiredService<ModerationDbContext>();
        var eventBus = scopedServices.GetRequiredService<IEventBus>();

        var (approved, reason) = AutomatedModerationRules.Evaluate(message);

        db.Decisions.Add(new ModerationDecision
        {
            ArticleId = message.ArticleId,
            Approved = approved,
            RejectionReason = reason,
        });
        await db.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            new ArticleModerationResultEvent(message.ArticleId, approved, reason, DateTimeOffset.UtcNow),
            cancellationToken);
    }
}
