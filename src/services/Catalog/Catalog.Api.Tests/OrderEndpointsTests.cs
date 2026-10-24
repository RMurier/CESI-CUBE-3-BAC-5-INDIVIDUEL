using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Collector.Contracts.Events;
using Collector.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catalog.Api.Tests;

[Trait("Category", "Integration")]
public sealed class OrderEndpointsTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private static HttpClient ClientFor(CatalogApiFactory f, Guid userId)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.IssueToken(userId, "Buyer", "Seller"));
        return client;
    }

    // Les catégories ne sont créées que par l'admin (POST /categories) : on seed directement en base.
    private static async Task<Guid> SeedCategoryAsync(CatalogApiFactory f)
    {
        using IServiceScope scope = f.Services.CreateScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Category category = new() { Name = $"Catégorie {Guid.NewGuid()}" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    /// <summary>Crée boutique + article puis force le statut Published (la modération passe par RabbitMQ, absent en test).</summary>
    private async Task<(Guid SellerId, Article Article)> PublishedArticleAsync(decimal price = 100m, decimal shipping = 10m, bool onboarded = true)
    {
        var sellerId = Guid.NewGuid();
        var seller = ClientFor(factory, sellerId);
        if (onboarded) await seller.PostAsync("/seller/payout-account/onboarding", content: null);
        var shop = (await (await seller.PostAsJsonAsync("/shops", new { Name = "Boutique" })).Content.ReadFromJsonAsync<Shop>())!;
        var article = (await (await seller.PostAsJsonAsync($"/shops/{shop.Id}/articles", new
        {
            Title = "Poster dédicacé",
            Description = "Poster dédicacé authentique, très bon état.",
            Price = price,
            ShippingCost = shipping,
            CategoryId = await SeedCategoryAsync(factory),
        })).Content.ReadFromJsonAsync<Article>())!;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        (await db.Articles.FindAsync(article.Id))!.Status = ArticleStatus.Published;
        await db.SaveChangesAsync();
        return (sellerId, article);
    }

    private static StringContent Webhook(string kind, Guid orderId) => new(
        JsonSerializer.Serialize(new { kind, orderId, sessionId = $"cs_test_{orderId:N}" }), Encoding.UTF8, "application/json");

    private async Task<HttpResponseMessage> PostWebhookAsync(string kind, Guid orderId, string signature = FakePaymentGateway.ValidSignature)
    {
        var anonymous = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe") { Content = Webhook(kind, orderId) };
        request.Headers.Add("Stripe-Signature", signature);
        return await anonymous.SendAsync(request);
    }

    private sealed record CreatedOrder(Guid OrderId, string CheckoutUrl);
    private sealed record OrderView(Guid Id, decimal TotalAmount, decimal CommissionAmount, string Status);

    [Fact]
    public async Task Buyer_creates_an_order_and_gets_a_checkout_url_with_the_5_percent_commission()
    {
        var (_, article) = await PublishedArticleAsync(price: 100m, shipping: 10m);
        var buyerId = Guid.NewGuid();
        var buyer = ClientFor(factory, buyerId);

        var response = await buyer.PostAsJsonAsync("/orders", new { ArticleId = article.Id });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreatedOrder>())!;
        Assert.StartsWith("https://checkout.test/", created.CheckoutUrl);

        var order = (await buyer.GetFromJsonAsync<OrderView>($"/orders/{created.OrderId}"))!;
        Assert.Equal("Pending", order.Status);
        Assert.Equal(110m, order.TotalAmount);
        Assert.Equal(5.50m, order.CommissionAmount); // 5 % de (100 + 10)
    }

    [Fact]
    public async Task The_commission_and_the_seller_account_are_sent_to_the_payment_provider()
    {
        var (sellerId, article) = await PublishedArticleAsync(price: 100m, shipping: 10m);
        var buyer = ClientFor(factory, Guid.NewGuid());

        var created = (await (await buyer.PostAsJsonAsync("/orders", new { ArticleId = article.Id })).Content.ReadFromJsonAsync<CreatedOrder>())!;

        var checkout = factory.Payments.Requests.Single(r => r.OrderId == created.OrderId);
        Assert.Equal(5.50m, checkout.CommissionAmount);               // prélevée par la plateforme
        Assert.Equal($"acct_test_{sellerId:N}", checkout.SellerAccountId); // le net part chez le vendeur
    }

    [Fact]
    public async Task An_article_whose_seller_has_not_set_up_payouts_cannot_be_bought()
    {
        var (_, article) = await PublishedArticleAsync(onboarded: false);

        var response = await ClientFor(factory, Guid.NewGuid()).PostAsJsonAsync("/orders", new { ArticleId = article.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_article_whose_seller_onboarding_is_unfinished_cannot_be_bought()
    {
        var (_, article) = await PublishedArticleAsync();
        factory.Payments.SellerAccountsReady = false;
        try
        {
            var response = await ClientFor(factory, Guid.NewGuid()).PostAsJsonAsync("/orders", new { ArticleId = article.Id });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        finally
        {
            factory.Payments.SellerAccountsReady = true;
        }
    }

    [Fact]
    public async Task A_signed_payment_webhook_marks_the_order_paid_sells_the_article_and_publishes_OrderPaidEvent()
    {
        var (sellerId, article) = await PublishedArticleAsync();
        var buyerId = Guid.NewGuid();
        var buyer = ClientFor(factory, buyerId);
        var created = (await (await buyer.PostAsJsonAsync("/orders", new { ArticleId = article.Id })).Content.ReadFromJsonAsync<CreatedOrder>())!;

        var webhook = await PostWebhookAsync("Completed", created.OrderId);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);

        var order = (await buyer.GetFromJsonAsync<OrderView>($"/orders/{created.OrderId}"))!;
        Assert.Equal("Paid", order.Status);
        Assert.Contains(factory.EventBus.PublishedOf<OrderPaidEvent>(),
            e => e.OrderId == created.OrderId && e.BuyerId == buyerId && e.SellerId == sellerId);

        var catalog = await buyer.GetFromJsonAsync<List<Article>>("/articles");
        Assert.DoesNotContain(catalog!, a => a.Id == article.Id);
    }

    [Fact]
    public async Task A_replayed_webhook_does_not_publish_a_second_OrderPaidEvent()
    {
        var (_, article) = await PublishedArticleAsync();
        var buyer = ClientFor(factory, Guid.NewGuid());
        var created = (await (await buyer.PostAsJsonAsync("/orders", new { ArticleId = article.Id })).Content.ReadFromJsonAsync<CreatedOrder>())!;

        await PostWebhookAsync("Completed", created.OrderId);
        await PostWebhookAsync("Completed", created.OrderId);

        Assert.Single(factory.EventBus.PublishedOf<OrderPaidEvent>(), e => e.OrderId == created.OrderId);
    }

    [Fact]
    public async Task A_webhook_with_an_invalid_signature_is_rejected_and_changes_nothing()
    {
        var (_, article) = await PublishedArticleAsync();
        var buyer = ClientFor(factory, Guid.NewGuid());
        var created = (await (await buyer.PostAsJsonAsync("/orders", new { ArticleId = article.Id })).Content.ReadFromJsonAsync<CreatedOrder>())!;

        var webhook = await PostWebhookAsync("Completed", created.OrderId, signature: "forged");

        Assert.Equal(HttpStatusCode.BadRequest, webhook.StatusCode);
        var order = (await buyer.GetFromJsonAsync<OrderView>($"/orders/{created.OrderId}"))!;
        Assert.Equal("Pending", order.Status);
    }

    [Fact]
    public async Task An_expired_checkout_cancels_the_order_and_the_article_stays_for_sale()
    {
        var (_, article) = await PublishedArticleAsync();
        var buyer = ClientFor(factory, Guid.NewGuid());
        var created = (await (await buyer.PostAsJsonAsync("/orders", new { ArticleId = article.Id })).Content.ReadFromJsonAsync<CreatedOrder>())!;

        await PostWebhookAsync("Expired", created.OrderId);

        var order = (await buyer.GetFromJsonAsync<OrderView>($"/orders/{created.OrderId}"))!;
        Assert.Equal("Cancelled", order.Status);
        var catalog = await buyer.GetFromJsonAsync<List<Article>>("/articles");
        Assert.Contains(catalog!, a => a.Id == article.Id);
    }

    [Fact]
    public async Task A_seller_cannot_buy_their_own_article()
    {
        var (sellerId, article) = await PublishedArticleAsync();

        var response = await ClientFor(factory, sellerId).PostAsJsonAsync("/orders", new { ArticleId = article.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_second_buyer_is_refused_while_a_payment_is_already_in_progress()
    {
        var (_, article) = await PublishedArticleAsync();
        await ClientFor(factory, Guid.NewGuid()).PostAsJsonAsync("/orders", new { ArticleId = article.Id });

        var second = await ClientFor(factory, Guid.NewGuid()).PostAsJsonAsync("/orders", new { ArticleId = article.Id });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task An_article_not_yet_published_cannot_be_bought()
    {
        var sellerId = Guid.NewGuid();
        var seller = ClientFor(factory, sellerId);
        var shop = (await (await seller.PostAsJsonAsync("/shops", new { Name = "B" })).Content.ReadFromJsonAsync<Shop>())!;
        var article = (await (await seller.PostAsJsonAsync($"/shops/{shop.Id}/articles", new
        {
            Title = "En attente", Description = "Article en attente de modération.", Price = 10m, ShippingCost = 1m,
            CategoryId = await SeedCategoryAsync(factory),
        })).Content.ReadFromJsonAsync<Article>())!;

        var response = await ClientFor(factory, Guid.NewGuid()).PostAsJsonAsync("/orders", new { ArticleId = article.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_third_party_cannot_read_someone_elses_order()
    {
        var (_, article) = await PublishedArticleAsync();
        var created = (await (await ClientFor(factory, Guid.NewGuid()).PostAsJsonAsync("/orders", new { ArticleId = article.Id }))
            .Content.ReadFromJsonAsync<CreatedOrder>())!;

        var response = await ClientFor(factory, Guid.NewGuid()).GetAsync($"/orders/{created.OrderId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ordering_without_a_token_is_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/orders", new { ArticleId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
