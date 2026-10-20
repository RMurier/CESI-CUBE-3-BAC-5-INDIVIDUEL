using Catalog.Api.Payments;
using Microsoft.Extensions.Options;
using Moq;
using Stripe;
using Stripe.Checkout;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// Vérifie le code qui appelle réellement le SDK Stripe (jamais exercé par les tests d'intégration,
/// qui remplacent IPaymentGateway par FakePaymentGateway) : construction des requêtes, arrondi des
/// montants en centimes, mapping des réponses. Les services Stripe (SessionService, AccountService,
/// AccountLinkService) sont mockables car leurs méthodes Create/GetAsync sont virtuelles.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StripePaymentGatewayTests
{
    private readonly Mock<SessionService> _sessionService = new();
    private readonly Mock<AccountService> _accountService = new();
    private readonly Mock<AccountLinkService> _accountLinkService = new();
    private readonly StripePaymentGateway _gateway;

    public StripePaymentGatewayTests()
    {
        _gateway = new StripePaymentGateway(
            Options.Create(new StripeOptions { SecretKey = "sk_test_dummy", WebhookSecret = "whsec_dummy" }),
            _sessionService.Object,
            _accountService.Object,
            _accountLinkService.Object);
    }

    private static CheckoutRequest SampleRequest(decimal itemPrice = 100m, decimal shippingCost = 0m, decimal commission = 5m) =>
        new(Guid.NewGuid(), "Vélo vintage", itemPrice, shippingCost, "acct_seller_1", commission, "https://shop/ok", "https://shop/ko");

    [Fact]
    public async Task CreateCheckoutSessionAsync_sends_the_commission_as_an_application_fee_in_cents()
    {
        SessionCreateOptions? captured = null;
        _sessionService
            .Setup(s => s.CreateAsync(It.IsAny<SessionCreateOptions>(), null, default))
            .Callback<SessionCreateOptions, RequestOptions?, CancellationToken>((o, _, _) => captured = o)
            .ReturnsAsync(new Session { Id = "cs_test_1", Url = "https://checkout.stripe.com/cs_test_1" });

        var request = SampleRequest(itemPrice: 19.99m, shippingCost: 4.5m, commission: 1.22m);
        var result = await _gateway.CreateCheckoutSessionAsync(request, default);

        Assert.Equal("cs_test_1", result.SessionId);
        Assert.Equal("https://checkout.stripe.com/cs_test_1", result.Url);
        Assert.NotNull(captured);
        Assert.Equal(122, captured!.PaymentIntentData!.ApplicationFeeAmount);
        Assert.Equal("acct_seller_1", captured.PaymentIntentData.TransferData!.Destination);
        Assert.Equal(request.OrderId.ToString(), captured.ClientReferenceId);
        Assert.Equal(request.SuccessUrl, captured.SuccessUrl);
        Assert.Equal(request.CancelUrl, captured.CancelUrl);
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_rounds_item_and_shipping_amounts_to_the_nearest_cent()
    {
        SessionCreateOptions? captured = null;
        _sessionService
            .Setup(s => s.CreateAsync(It.IsAny<SessionCreateOptions>(), null, default))
            .Callback<SessionCreateOptions, RequestOptions?, CancellationToken>((o, _, _) => captured = o)
            .ReturnsAsync(new Session { Id = "cs_test_2", Url = "https://checkout.stripe.com/cs_test_2" });

        // 19.995 -> 2000 centimes (arrondi au plus proche, demi tranche vers le haut).
        await _gateway.CreateCheckoutSessionAsync(SampleRequest(itemPrice: 19.995m, shippingCost: 2.005m), default);

        Assert.Equal(2, captured!.LineItems!.Count);
        Assert.Equal(2000, captured.LineItems[0].PriceData!.UnitAmount);
        Assert.Equal(201, captured.LineItems[1].PriceData!.UnitAmount);
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_omits_the_shipping_line_item_when_shipping_is_free()
    {
        _sessionService
            .Setup(s => s.CreateAsync(It.IsAny<SessionCreateOptions>(), null, default))
            .Callback<SessionCreateOptions, RequestOptions?, CancellationToken>((o, _, _) => Assert.Single(o.LineItems!))
            .ReturnsAsync(new Session { Id = "cs_test_3", Url = "https://checkout.stripe.com/cs_test_3" });

        await _gateway.CreateCheckoutSessionAsync(SampleRequest(shippingCost: 0m), default);
    }

    [Fact]
    public async Task CreateSellerAccountAsync_requests_an_express_account_in_France_with_the_seller_id_in_metadata()
    {
        var sellerId = Guid.NewGuid();
        AccountCreateOptions? captured = null;
        _accountService
            .Setup(s => s.CreateAsync(It.IsAny<AccountCreateOptions>(), null, default))
            .Callback<AccountCreateOptions, RequestOptions?, CancellationToken>((o, _, _) => captured = o)
            .ReturnsAsync(new Account { Id = "acct_new" });

        var accountId = await _gateway.CreateSellerAccountAsync(sellerId, default);

        Assert.Equal("acct_new", accountId);
        Assert.Equal("express", captured!.Type);
        Assert.Equal("FR", captured.Country);
        Assert.Equal(sellerId.ToString(), captured.Metadata!["seller_id"]);
    }

    [Fact]
    public async Task CreateOnboardingLinkAsync_returns_the_hosted_link_url()
    {
        _accountLinkService
            .Setup(s => s.CreateAsync(It.IsAny<AccountLinkCreateOptions>(), null, default))
            .ReturnsAsync(new AccountLink { Url = "https://connect.stripe.com/setup/abc" });

        var url = await _gateway.CreateOnboardingLinkAsync("acct_1", "https://shop/return", "https://shop/refresh", default);

        Assert.Equal("https://connect.stripe.com/setup/abc", url);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("inactive", false)]
    [InlineData(null, false)]
    public async Task IsSellerAccountReadyAsync_reflects_the_transfers_capability(string? transfersStatus, bool expected)
    {
        _accountService
            .Setup(s => s.GetAsync("acct_1", It.IsAny<AccountGetOptions>(), null, default))
            .ReturnsAsync(new Account { Capabilities = new AccountCapabilities { Transfers = transfersStatus } });

        Assert.Equal(expected, await _gateway.IsSellerAccountReadyAsync("acct_1", default));
    }
}

/// <summary>DisabledPaymentGateway (Stripe:SecretKey absente) : toute opération échoue plutôt que de planter le démarrage.</summary>
[Trait("Category", "Unit")]
public sealed class DisabledPaymentGatewayTests
{
    private readonly DisabledPaymentGateway _gateway = new();

    [Fact]
    public async Task CreateCheckoutSessionAsync_throws_PaymentsNotConfigured()
    {
        await Assert.ThrowsAsync<PaymentsNotConfiguredException>(() =>
            _gateway.CreateCheckoutSessionAsync(new CheckoutRequest(Guid.NewGuid(), "x", 1m, 0m, "acct", 0m, "ok", "ko"), default));
    }

    [Fact]
    public async Task CreateSellerAccountAsync_throws_PaymentsNotConfigured()
    {
        await Assert.ThrowsAsync<PaymentsNotConfiguredException>(() => _gateway.CreateSellerAccountAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task CreateOnboardingLinkAsync_throws_PaymentsNotConfigured()
    {
        await Assert.ThrowsAsync<PaymentsNotConfiguredException>(() => _gateway.CreateOnboardingLinkAsync("acct", "ok", "ko", default));
    }

    [Fact]
    public async Task IsSellerAccountReadyAsync_throws_PaymentsNotConfigured()
    {
        await Assert.ThrowsAsync<PaymentsNotConfiguredException>(() => _gateway.IsSellerAccountReadyAsync("acct", default));
    }

    [Fact]
    public void ParseWebhook_always_returns_null()
    {
        Assert.Null(_gateway.ParseWebhook("{}", "sig"));
    }
}
