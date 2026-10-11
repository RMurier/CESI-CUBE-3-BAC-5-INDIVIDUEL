using Collector.Contracts.Events;
using Collector.Messaging;
using Microsoft.Extensions.Options;
using Moderation.Api.Domain;
using Moderation.Api.Infrastructure;

namespace Moderation.Api.Messaging;

/// <summary>
/// Reçoit chaque changement de prix (exigence contexte : "la notification de changement de prix doit
/// être aussi reçue par un composant back-office de détection des fraudes"). Flague comme suspecte
/// toute variation de plus de 50 % — heuristique simple pour le POC.
/// </summary>
public sealed class PriceChangedConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<PriceChangedConsumer> logger)
    : RabbitMqConsumer<PriceChangedEvent>(options, "moderation.price-changed", scopeFactory, logger)
{
    private const double SuspiciousVariationThreshold = 0.5;

    protected override async Task HandleAsync(
        PriceChangedEvent message,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var db = scopedServices.GetRequiredService<ModerationDbContext>();

        var variation = message.PreviousPrice == 0
            ? 1.0
            : (double)Math.Abs(message.NewPrice - message.PreviousPrice) / (double)message.PreviousPrice;

        var suspicious = variation > SuspiciousVariationThreshold;

        db.Decisions.Add(new ModerationDecision
        {
            ArticleId = message.ArticleId,
            Approved = !suspicious,
            RejectionReason = suspicious
                ? $"Variation de prix suspecte : {variation:P0} ({message.PreviousPrice:C} → {message.NewPrice:C})"
                : null,
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
