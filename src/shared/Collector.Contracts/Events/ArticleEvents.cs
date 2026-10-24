namespace Collector.Contracts.Events;

/// <summary>Publié par Catalog quand un vendeur soumet un article à la vente.</summary>
public sealed record ArticleSubmittedEvent(
    Guid ArticleId,
    Guid ShopId,
    Guid SellerId,
    string Title,
    string Description,
    decimal Price,
    decimal ShippingCost,
    DateTimeOffset SubmittedAt);

/// <summary>Publié par Moderation une fois le contrôle automatisé effectué.</summary>
public sealed record ArticleModerationResultEvent(
    Guid ArticleId,
    bool Approved,
    string? RejectionReason,
    DateTimeOffset EvaluatedAt);

/// <summary>Publié par Catalog quand un article passe effectivement en ligne (après approbation).</summary>
public sealed record ArticlePublishedEvent(
    Guid ArticleId,
    Guid ShopId,
    string Title,
    decimal Price,
    DateTimeOffset PublishedAt);

/// <summary>Publié par Catalog quand le prix d'un article déjà en ligne change.</summary>
/// <remarks>
/// Title dénormalisé (même choix que OrderPaidEvent.ArticleTitle) : Notification en a besoin pour
/// matcher les Subscriptions par mot-clé sans rappeler Catalog, Moderation pour son log de détection.
/// </remarks>
public sealed record PriceChangedEvent(
    Guid ArticleId,
    string Title,
    decimal PreviousPrice,
    decimal NewPrice,
    DateTimeOffset ChangedAt);
