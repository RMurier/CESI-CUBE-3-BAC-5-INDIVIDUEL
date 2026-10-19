namespace Collector.Contracts.Events;

/// <summary>Publié par Catalog quand le paiement d'une commande est confirmé (webhook Stripe signé).</summary>
public sealed record OrderPaidEvent(
    Guid OrderId,
    Guid ArticleId,
    string ArticleTitle,
    Guid BuyerId,
    Guid SellerId,
    decimal TotalAmount,
    decimal CommissionAmount,
    DateTimeOffset PaidAt);
