namespace Catalog.Api.Domain;

/// <summary>Créées uniquement par l'admin (exigence contexte), pas par les vendeurs.</summary>
public sealed class Category
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
