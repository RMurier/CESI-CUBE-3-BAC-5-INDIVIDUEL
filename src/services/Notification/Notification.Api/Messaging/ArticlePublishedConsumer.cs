using Collector.Contracts.Events;
using Collector.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notification.Api.Domain;
using Notification.Api.Infrastructure;

namespace Notification.Api.Messaging;

/// <summary>Notifie (in-app + email) les acheteurs dont un centre d'intérêt correspond au titre de l'article.</summary>
public sealed class ArticlePublishedConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<ArticlePublishedConsumer> logger)
    : RabbitMqConsumer<ArticlePublishedEvent>(options, "notification.article-published", scopeFactory, logger)
{
    protected override async Task HandleAsync(
        ArticlePublishedEvent publishedEvent,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var db = scopedServices.GetRequiredService<NotificationDbContext>();
        var emailSender = scopedServices.GetRequiredService<IEmailSender>();

        var matchingSubscriptions = await db.Subscriptions
            .Where(s => EF.Functions.Like(publishedEvent.Title, $"%{s.Keyword}%"))
            .ToListAsync(cancellationToken);

        foreach (var subscription in matchingSubscriptions)
        {
            var message = $"Nouvel article correspondant à votre intérêt « {subscription.Keyword} » : {publishedEvent.Title} ({publishedEvent.Price:C})";

            db.Notifications.Add(new UserNotification { UserId = subscription.UserId, Message = message });
            await emailSender.SendAsync(subscription.UserId, "Nouvel article Collector.shop", message, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
