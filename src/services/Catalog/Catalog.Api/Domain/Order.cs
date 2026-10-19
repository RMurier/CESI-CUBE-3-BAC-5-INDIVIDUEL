namespace Catalog.Api.Domain;

public enum OrderStatus
{
    Pending,
    Paid,
    Cancelled,
}

public sealed class Order
{
    /// <summary>Commission prélevée par Collector sur chaque transaction (règle métier, README section 1).</summary>
    public const decimal CommissionRate = 0.05m;

    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid ArticleId { get; init; }
    public required string ArticleTitle { get; init; }

    /// <summary>Ids utilisateur (service Identity) — pas de clé étrangère cross-service.</summary>
    public required Guid BuyerId { get; init; }
    public required Guid SellerId { get; init; }

    // Prix figés à la commande : un changement de prix ultérieur ne modifie pas une commande en cours.
    public required decimal ItemPrice { get; init; }
    public required decimal ShippingCost { get; init; }

    /// <summary>
    /// Commission Collector, figée à la commande et persistée : un changement futur du taux ne doit pas
    /// réécrire l'historique. C'est aussi le montant exact transmis à Stripe (application_fee_amount).
    /// </summary>
    public required decimal CommissionAmount { get; init; }

    /// <summary>Compte Stripe Connect du vendeur qui reçoit le net (total - commission).</summary>
    public required string SellerAccountId { get; init; }

    public decimal TotalAmount => ItemPrice + ShippingCost;
    public decimal SellerAmount => TotalAmount - CommissionAmount;

    public static decimal ComputeCommission(decimal totalAmount) =>
        Math.Round(totalAmount * CommissionRate, 2, MidpointRounding.AwayFromZero);

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string? CheckoutSessionId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
}
