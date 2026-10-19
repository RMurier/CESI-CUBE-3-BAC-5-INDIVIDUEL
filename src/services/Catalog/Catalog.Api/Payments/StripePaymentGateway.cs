using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Catalog.Api.Payments;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Clé secrète (sk_test_... en dev/uat). Arrive via Infisical, jamais en dur.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Secret de signature du endpoint webhook (whsec_...).</summary>
    public string? WebhookSecret { get; set; }
}

/// <summary>
/// Stripe Checkout hébergé : la saisie de carte se fait sur la page Stripe, aucune donnée de carte ne
/// transite ni n'est stockée chez Collector (périmètre PCI DSS minimal, SAQ A).
/// </summary>
public sealed class StripePaymentGateway(IOptions<StripeOptions> options) : IPaymentGateway
{
    private const string Currency = "eur";
    private readonly StripeOptions _options = options.Value;
    private readonly StripeClient _client = new(options.Value.SecretKey);

    public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var lineItems = new List<SessionLineItemOptions> { LineItem(request.ArticleTitle, request.ItemPrice) };
        if (request.ShippingCost > 0) lineItems.Add(LineItem("Frais de port", request.ShippingCost));

        var session = await new SessionService(_client).CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            LineItems = lineItems,
            ClientReferenceId = request.OrderId.ToString(),
            Metadata = new Dictionary<string, string> { ["order_id"] = request.OrderId.ToString() },
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            // Destination charge : Stripe transfère (total - commission) au vendeur et conserve la
            // commission sur le compte de la plateforme. Montant exact = Order.CommissionAmount.
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                ApplicationFeeAmount = ToCents(request.CommissionAmount),
                TransferData = new SessionPaymentIntentDataTransferDataOptions { Destination = request.SellerAccountId },
                Metadata = new Dictionary<string, string> { ["order_id"] = request.OrderId.ToString() },
            },
        }, cancellationToken: cancellationToken);

        return new CheckoutSessionResult(session.Id, session.Url);
    }

    public async Task<string> CreateSellerAccountAsync(Guid sellerId, CancellationToken cancellationToken)
    {
        var account = await new AccountService(_client).CreateAsync(new AccountCreateOptions
        {
            Type = "express",
            Country = "FR",
            Capabilities = new AccountCapabilitiesOptions
            {
                CardPayments = new AccountCapabilitiesCardPaymentsOptions { Requested = true },
                Transfers = new AccountCapabilitiesTransfersOptions { Requested = true },
            },
            Metadata = new Dictionary<string, string> { ["seller_id"] = sellerId.ToString() },
        }, cancellationToken: cancellationToken);
        return account.Id;
    }

    public async Task<string> CreateOnboardingLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken cancellationToken)
    {
        var link = await new AccountLinkService(_client).CreateAsync(new AccountLinkCreateOptions
        {
            Account = accountId,
            Type = "account_onboarding",
            ReturnUrl = returnUrl,
            RefreshUrl = refreshUrl,
        }, cancellationToken: cancellationToken);
        return link.Url;
    }

    public async Task<bool> IsSellerAccountReadyAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await new AccountService(_client).GetAsync(accountId, cancellationToken: cancellationToken);
        return account.Capabilities?.Transfers == "active";
    }

    public PaymentWebhookEvent? ParseWebhook(string payload, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(_options.WebhookSecret)) return null;

        Event stripeEvent;
        try
        {
            // throwOnApiVersionMismatch: false — la version d'API du compte Stripe peut être plus récente que le SDK.
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _options.WebhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException)
        {
            return null;
        }

        if (stripeEvent.Data.Object is not Session session) return null;
        if (!Guid.TryParse(session.ClientReferenceId, out var orderId)) return null;

        return stripeEvent.Type switch
        {
            EventTypes.CheckoutSessionCompleted or EventTypes.CheckoutSessionAsyncPaymentSucceeded
                when session.PaymentStatus == "paid" => new PaymentWebhookEvent(PaymentWebhookKind.Completed, orderId, session.Id),
            EventTypes.CheckoutSessionExpired => new PaymentWebhookEvent(PaymentWebhookKind.Expired, orderId, session.Id),
            _ => null,
        };
    }

    private static long ToCents(decimal amount) => (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private static SessionLineItemOptions LineItem(string name, decimal amount) => new()
    {
        Quantity = 1,
        PriceData = new SessionLineItemPriceDataOptions
        {
            Currency = Currency,
            UnitAmount = ToCents(amount),
            ProductData = new SessionLineItemPriceDataProductDataOptions { Name = name },
        },
    };
}

/// <summary>Utilisé quand Stripe:SecretKey n'est pas configurée : les commandes répondent 503 au lieu de planter au démarrage.</summary>
public sealed class DisabledPaymentGateway : IPaymentGateway
{
    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken cancellationToken) =>
        throw new PaymentsNotConfiguredException();

    public Task<string> CreateSellerAccountAsync(Guid sellerId, CancellationToken cancellationToken) =>
        throw new PaymentsNotConfiguredException();

    public Task<string> CreateOnboardingLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken cancellationToken) =>
        throw new PaymentsNotConfiguredException();

    public Task<bool> IsSellerAccountReadyAsync(string accountId, CancellationToken cancellationToken) =>
        throw new PaymentsNotConfiguredException();

    public PaymentWebhookEvent? ParseWebhook(string payload, string? signatureHeader) => null;
}

public sealed class PaymentsNotConfiguredException() : InvalidOperationException("Paiement non configuré (Stripe:SecretKey manquante).");
