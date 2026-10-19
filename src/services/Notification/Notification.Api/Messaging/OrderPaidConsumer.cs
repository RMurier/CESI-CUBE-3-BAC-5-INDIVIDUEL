using Collector.Contracts.Events;
using Collector.Messaging;
using Microsoft.Extensions.Options;
using Notification.Api.Domain;
using Notification.Api.Infrastructure;

namespace Notification.Api.Messaging;

/// <summary>Prévient l'acheteur (confirmation) et le vendeur (article vendu) une fois le paiement confirmé.</summary>
public sealed class OrderPaidConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderPaidConsumer> logger)
    : RabbitMqConsumer<OrderPaidEvent>(options, "notification.order-paid", scopeFactory, logger)
{
    protected override async Task HandleAsync(
        OrderPaidEvent order,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var db = scopedServices.GetRequiredService<NotificationDbContext>();
        var emailSender = scopedServices.GetRequiredService<IEmailSender>();

        var buyerMessage = $"Paiement confirmé : {order.ArticleTitle} ({order.TotalAmount:0.00} €). Le vendeur va préparer l'envoi.";
        var sellerMessage = $"Votre article « {order.ArticleTitle} » est vendu ({order.TotalAmount:0.00} €, dont {order.CommissionAmount:0.00} € de commission Collector).";

        db.Notifications.Add(new UserNotification { UserId = order.BuyerId, Message = buyerMessage });
        db.Notifications.Add(new UserNotification { UserId = order.SellerId, Message = sellerMessage });
        await db.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(order.BuyerId, "Commande confirmée - Collector.shop", buyerMessage, cancellationToken);
        await emailSender.SendAsync(order.SellerId, "Article vendu - Collector.shop", sellerMessage, cancellationToken);
    }
}
