namespace Identity.Api.Domain;

public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Email { get; init; }
    public required string PasswordHash { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Tout utilisateur inscrit est acheteur par défaut (parcours catalogue libre, achat requiert un compte).</summary>
    public bool IsBuyer { get; set; } = true;

    /// <summary>Un acheteur peut en plus devenir vendeur (règle métier Collector.shop).</summary>
    public bool IsSeller { get; set; }

    public bool IsAdmin { get; set; }

    /// <summary>Mis à jour par EmailConfirmationService quand le lien reçu par email est cliqué.
    /// Purement informatif pour l'instant : rien ne bloque un compte non confirmé.</summary>
    public bool IsEmailConfirmed { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public IEnumerable<string> Roles()
    {
        if (IsBuyer) yield return "Buyer";
        if (IsSeller) yield return "Seller";
        if (IsAdmin) yield return "Admin";
    }
}
