namespace Catalog.Api.Domain;

/// <summary>
/// Compte Stripe Connect (Express) d'un vendeur : sans lui, impossible de lui reverser le net d'une vente.
/// Stripe héberge l'onboarding (identité, IBAN) : Collector ne stocke aucune donnée bancaire.
/// </summary>
public sealed class SellerPayoutAccount
{
    /// <summary>Id utilisateur vendeur (service Identity), clé primaire : un compte de paiement par vendeur.</summary>
    public required Guid SellerId { get; init; }
    public required string StripeAccountId { get; init; }

    /// <summary>Le compte peut recevoir des transferts (capability "transfers" active côté Stripe).</summary>
    public bool Ready { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
