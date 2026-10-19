using System.Text.Json;
using Catalog.Api.Payments;

namespace Catalog.Api.Tests;

/// <summary>
/// Double de test : pas de réseau vers Stripe. Le "webhook" est un JSON {kind, orderId, sessionId}
/// et la seule signature acceptée est <see cref="ValidSignature"/> (la vraie vérification HMAC est celle du SDK Stripe).
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public const string ValidSignature = "valid-signature";

    public List<CheckoutRequest> Requests { get; } = [];

    /// <summary>Simule l'état de l'onboarding Stripe des comptes vendeurs (capability "transfers").</summary>
    public bool SellerAccountsReady { get; set; } = true;

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(new CheckoutSessionResult($"cs_test_{request.OrderId:N}", $"https://checkout.test/{request.OrderId}"));
    }

    public Task<string> CreateSellerAccountAsync(Guid sellerId, CancellationToken cancellationToken) =>
        Task.FromResult($"acct_test_{sellerId:N}");

    public Task<string> CreateOnboardingLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken cancellationToken) =>
        Task.FromResult($"https://connect.test/onboarding/{accountId}");

    public Task<bool> IsSellerAccountReadyAsync(string accountId, CancellationToken cancellationToken) =>
        Task.FromResult(SellerAccountsReady);

    public PaymentWebhookEvent? ParseWebhook(string payload, string? signatureHeader)
    {
        if (signatureHeader != ValidSignature) return null;
        var json = JsonDocument.Parse(payload).RootElement;
        return new PaymentWebhookEvent(
            Enum.Parse<PaymentWebhookKind>(json.GetProperty("kind").GetString()!),
            json.GetProperty("orderId").GetGuid(),
            json.GetProperty("sessionId").GetString()!);
    }
}
