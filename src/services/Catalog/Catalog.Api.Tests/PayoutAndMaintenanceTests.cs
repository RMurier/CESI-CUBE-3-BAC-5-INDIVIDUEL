using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Collector.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catalog.Api.Tests;

[Trait("Category", "Integration")]
public sealed class PayoutAndMaintenanceTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private const string MaintenanceKey = "test-maintenance-key";

    private static HttpClient ClientFor(CatalogApiFactory f, Guid userId, params string[] roles)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.IssueToken(userId, roles));
        return client;
    }

    private HttpClient MaintenanceClient(string? key)
    {
        var client = factory
            .WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
                c.AddInMemoryCollection(new Dictionary<string, string?> { ["Maintenance:ApiKey"] = MaintenanceKey })))
            .CreateClient();
        if (key is not null) client.DefaultRequestHeaders.Add("X-Maintenance-Key", key);
        return client;
    }

    private sealed record OnboardingResponse(string Url);
    private sealed record PayoutStatus(bool Configured, bool Ready);

    [Fact]
    public async Task A_seller_gets_a_Stripe_onboarding_link_and_then_a_ready_status()
    {
        var seller = ClientFor(factory, Guid.NewGuid(), "Buyer", "Seller");

        var onboarding = await seller.PostAsync("/seller/payout-account/onboarding", content: null);
        Assert.Equal(HttpStatusCode.OK, onboarding.StatusCode);
        Assert.StartsWith("https://connect.test/", (await onboarding.Content.ReadFromJsonAsync<OnboardingResponse>())!.Url);

        var status = (await seller.GetFromJsonAsync<PayoutStatus>("/seller/payout-account"))!;
        Assert.True(status.Configured);
        Assert.True(status.Ready);
    }

    [Fact]
    public async Task A_buyer_who_is_not_a_seller_cannot_start_payout_onboarding()
    {
        var buyerOnly = ClientFor(factory, Guid.NewGuid(), "Buyer");

        var response = await buyerOnly.PostAsync("/seller/payout-account/onboarding", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Maintenance_endpoints_are_disabled_when_no_key_is_configured()
    {
        var response = await factory.CreateClient().PostAsync("/internal/maintenance/expire-pending-orders", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Maintenance_endpoints_reject_a_missing_or_wrong_key(string? key)
    {
        var response = await MaintenanceClient(key).PostAsync("/internal/maintenance/expire-pending-orders", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stale_pending_orders_are_cancelled_and_recent_ones_are_left_alone()
    {
        Guid staleId, freshId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Order New(DateTimeOffset createdAt) => new()
            {
                ArticleId = Guid.NewGuid(), ArticleTitle = "x", BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
                ItemPrice = 10m, ShippingCost = 0m, CommissionAmount = 0.50m, SellerAccountId = "acct_x", CreatedAt = createdAt,
            };
            var stale = New(DateTimeOffset.UtcNow.AddHours(-2));
            var fresh = New(DateTimeOffset.UtcNow.AddMinutes(-5));
            (staleId, freshId) = (stale.Id, fresh.Id);
            db.Orders.AddRange(stale, fresh);
            await db.SaveChangesAsync();
        }

        var response = await MaintenanceClient(MaintenanceKey).PostAsync("/internal/maintenance/expire-pending-orders", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(OrderStatus.Cancelled, (await verifyDb.Orders.FindAsync(staleId))!.Status);
        Assert.Equal(OrderStatus.Pending, (await verifyDb.Orders.FindAsync(freshId))!.Status);
    }

    [Fact]
    public async Task The_commission_report_sums_the_paid_orders_of_the_period()
    {
        var paidAt = new DateTimeOffset(2030, 1, 15, 12, 0, 0, TimeSpan.Zero);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            db.Orders.Add(new Order
            {
                ArticleId = Guid.NewGuid(), ArticleTitle = "x", BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
                ItemPrice = 100m, ShippingCost = 10m, CommissionAmount = 5.50m, SellerAccountId = "acct_x",
                Status = OrderStatus.Paid, PaidAt = paidAt,
            });
            await db.SaveChangesAsync();
        }

        var from = Uri.EscapeDataString("2030-01-15T00:00:00Z");
        var to = Uri.EscapeDataString("2030-01-16T00:00:00Z");
        var report = await MaintenanceClient(MaintenanceKey).GetFromJsonAsync<Report>($"/internal/reports/commissions?from={from}&to={to}");

        Assert.Equal(1, report!.Orders);
        Assert.Equal(110m, report.TotalAmount);
        Assert.Equal(5.50m, report.CommissionAmount);
        Assert.Equal(104.50m, report.SellerAmount);
    }

    private sealed record Report(int Orders, decimal TotalAmount, decimal CommissionAmount, decimal SellerAmount);
}
