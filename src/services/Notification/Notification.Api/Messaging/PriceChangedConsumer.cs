using Collector.Contracts.Events;
using Collector.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notification.Api.Domain;
using Notification.Api.Infrastructure;

namespace Notification.Api.Messaging;

/// <summary>
/// Exigence contexte : "Les acheteurs ayant de l'intérêt pour cet article doivent en être informés"
/// d'un changement de prix. Même mécanisme de matching que ArticlePublishedConsumer (mot-clé de
/// Subscription contenu dans le titre) — pas de "liste d'articles suivis" dédiée dans le modèle actuel.
/// </summary>
public sealed class PriceChangedConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<PriceChangedConsumer> logger)
    : RabbitMqConsumer<PriceChangedEvent>(options, "notification.price-changed", scopeFactory, logger)
{
    protected override async Task HandleAsync(
        PriceChangedEvent priceChange,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        NotificationDbContext db = scopedServices.GetRequiredService<NotificationDbContext>();
        IEmailSender emailSender = scopedServices.GetRequiredService<IEmailSender>();

        List<InterestSubscription> matchingSubscriptions = await db.Subscriptions
            .Where(s => priceChange.Title.Contains(s.Keyword))
            .ToListAsync(cancellationToken);

        foreach (InterestSubscription subscription in matchingSubscriptions)
        {
            string direction = priceChange.NewPrice < priceChange.PreviousPrice ? "baissé" : "augmenté";
            string message = $"Le prix de « {priceChange.Title} » a {direction} : {priceChange.PreviousPrice:C} → {priceChange.NewPrice:C}";

            db.Notifications.Add(new UserNotification { UserId = subscription.UserId, Message = message });
            await emailSender.SendAsync(subscription.UserId, "Changement de prix - Collector.shop", message, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
