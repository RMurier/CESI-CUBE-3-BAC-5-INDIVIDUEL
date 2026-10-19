using System.Security.Cryptography;
using System.Text;
using Catalog.Api.Payments;
using Microsoft.Extensions.Options;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>Vérifie la vraie logique de signature du SDK Stripe (HMAC-SHA256), sans réseau ni base.</summary>
[Trait("Category", "Unit")]
public sealed class StripeWebhookSignatureTests
{
    private const string WebhookSecret = "whsec_test_secret";
    private readonly StripePaymentGateway _gateway = new(Options.Create(new StripeOptions
    {
        SecretKey = "sk_test_dummy",
        WebhookSecret = WebhookSecret,
    }));

    private static string EventJson(string type, Guid orderId, string paymentStatus = "paid") => """
        {"id":"evt_1","object":"event","api_version":"2024-06-20","created":1700000000,"livemode":false,"pending_webhooks":1,"request":{"id":null,"idempotency_key":null},
         "type":"TYPE",
         "data":{"object":{"id":"cs_test_1","object":"checkout.session","livemode":false,"client_reference_id":"ORDER","payment_status":"STATUS"}}}
        """.Replace("TYPE", type).Replace("ORDER", orderId.ToString()).Replace("STATUS", paymentStatus);

    private static string Sign(string payload, string secret = WebhookSecret, long? timestamp = null)
    {
        var t = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{t}.{payload}"));
        return $"t={t},v1={Convert.ToHexStringLower(hash)}";
    }

    [Fact]
    public void A_correctly_signed_paid_session_is_translated_to_a_Completed_event()
    {
        var orderId = Guid.NewGuid();
        var payload = EventJson("checkout.session.completed", orderId);

        var result = _gateway.ParseWebhook(payload, Sign(payload));

        Assert.Equal(new PaymentWebhookEvent(PaymentWebhookKind.Completed, orderId, "cs_test_1"), result);
    }

    [Fact]
    public void A_payload_signed_with_another_secret_is_rejected()
    {
        var payload = EventJson("checkout.session.completed", Guid.NewGuid());

        Assert.Null(_gateway.ParseWebhook(payload, Sign(payload, secret: "whsec_attacker")));
    }

    [Fact]
    public void A_tampered_payload_is_rejected()
    {
        var orderId = Guid.NewGuid();
        var payload = EventJson("checkout.session.completed", orderId);
        var signature = Sign(payload);

        Assert.Null(_gateway.ParseWebhook(EventJson("checkout.session.completed", Guid.NewGuid()), signature));
    }

    [Fact]
    public void A_replayed_old_signature_is_rejected()
    {
        var payload = EventJson("checkout.session.completed", Guid.NewGuid());
        var tenMinutesAgo = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();

        Assert.Null(_gateway.ParseWebhook(payload, Sign(payload, timestamp: tenMinutesAgo)));
    }

    [Fact]
    public void A_completed_session_that_is_not_paid_yet_is_ignored()
    {
        var payload = EventJson("checkout.session.completed", Guid.NewGuid(), paymentStatus: "unpaid");

        Assert.Null(_gateway.ParseWebhook(payload, Sign(payload)));
    }

    [Fact]
    public void A_missing_signature_header_is_rejected()
    {
        Assert.Null(_gateway.ParseWebhook(EventJson("checkout.session.completed", Guid.NewGuid()), null));
    }
}
