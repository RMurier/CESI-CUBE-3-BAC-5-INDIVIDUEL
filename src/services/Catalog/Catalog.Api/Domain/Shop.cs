namespace Catalog.Api.Domain;

public sealed class Shop
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Id de l'utilisateur vendeur (service Identity) — pas de clé étrangère cross-service.</summary>
    public required Guid SellerId { get; init; }

    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public ICollection<Article> Articles { get; init; } = [];
}
