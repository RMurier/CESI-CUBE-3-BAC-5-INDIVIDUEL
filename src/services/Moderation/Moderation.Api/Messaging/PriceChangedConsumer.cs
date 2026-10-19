using Collector.Contracts.Events;
using Collector.Messaging;
using Microsoft.Extensions.Options;
using Moderation.Api.Domain;
using Moderation.Api.Infrastructure;

namespace Moderation.Api.Messaging;

/// <summary>
/// Reçoit chaque changement de prix (exigence contexte : "la notification de changement de prix doit
/// être aussi reçue par un composant back-office de détection des fraudes"). Flague comme suspecte
/// les cas jugés suspects par l'<see cref="IFraudDetector"/> configuré (heuristique par règles par défaut).
/// </summary>
public sealed class PriceChangedConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<PriceChangedConsumer> logger)
    : RabbitMqConsumer<PriceChangedEvent>(options, "moderation.price-changed", scopeFactory, logger)
{
    protected override async Task HandleAsync(
        PriceChangedEvent message,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var db = scopedServices.GetRequiredService<ModerationDbContext>();

        var verdict = scopedServices.GetRequiredService<IFraudDetector>().AssessPriceChange(message);

        db.Decisions.Add(new ModerationDecision
        {
            ArticleId = message.ArticleId,
            Approved = !verdict.Suspicious,
            RejectionReason = verdict.Reason,
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
