namespace Catalog.Api.Payments;

public sealed record CheckoutRequest(
    Guid OrderId,
    string ArticleTitle,
    decimal ItemPrice,
    decimal ShippingCost,
    string SellerAccountId,
    decimal CommissionAmount,
    string SuccessUrl,
    string CancelUrl);

public sealed record CheckoutSessionResult(string SessionId, string Url);

public enum PaymentWebhookKind
{
    Completed,
    Expired,
}

public sealed record PaymentWebhookEvent(PaymentWebhookKind Kind, Guid OrderId, string SessionId);

/// <summary>
/// Frontière avec le prestataire de paiement : le reste du service ne connaît pas Stripe, ce qui
/// permet de tester le flux de commande sans réseau et de changer de PSP sans toucher au domaine.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Ouvre une session de paiement. Le net (total - commission) est transféré au compte Connect du
    /// vendeur ; la commission reste sur le compte de la plateforme (destination charge + application fee).
    /// </summary>
    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>Crée le compte Connect Express du vendeur et renvoie son identifiant.</summary>
    Task<string> CreateSellerAccountAsync(Guid sellerId, CancellationToken cancellationToken);

    /// <summary>Lien d'onboarding hébergé par Stripe (identité, IBAN) : usage unique, expire vite.</summary>
    Task<string> CreateOnboardingLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken cancellationToken);

    /// <summary>Vrai si le compte peut recevoir des transferts (onboarding terminé côté Stripe).</summary>
    Task<bool> IsSellerAccountReadyAsync(string accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Vérifie la signature du webhook puis le traduit. Renvoie null si la signature est invalide
    /// (requête non émise par le PSP) ou si l'évènement ne concerne pas une commande Collector.
    /// </summary>
    PaymentWebhookEvent? ParseWebhook(string payload, string? signatureHeader);
}
