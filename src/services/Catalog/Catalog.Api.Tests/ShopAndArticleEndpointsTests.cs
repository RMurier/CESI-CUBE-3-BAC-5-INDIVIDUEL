using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Collector.Contracts.Events;
using Collector.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catalog.Api.Tests;

[Trait("Category", "Integration")]
public sealed class ShopAndArticleEndpointsTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private readonly HttpClient _client = CreateAuthenticatedClient(factory, out _);

    private static HttpClient CreateAuthenticatedClient(CatalogApiFactory factory, out Guid sellerId)
    {
        sellerId = Guid.NewGuid();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.IssueToken(sellerId, "Buyer", "Seller"));
        return client;
    }

    // Les catégories ne sont créées que par l'admin (POST /categories) : on seed directement en base
    // plutôt que de passer par un client authentifié "Admin" dans chaque test d'article.
    private static async Task<Guid> SeedCategoryAsync(CatalogApiFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Category category = new() { Name = $"Catégorie {Guid.NewGuid()}" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    // Échoue avec le corps de la réponse en message d'assertion plutôt qu'une JsonException opaque
    // si le serveur renvoie autre chose que le code attendu (ex: message d'exception sur un 500).
    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {expected} but got {response.StatusCode}. Body: {body}");
        }
    }

    [Fact]
    public async Task Creating_an_article_publishes_ArticleSubmittedEvent_and_keeps_it_out_of_the_public_catalog()
    {
        var shopResponse = await _client.PostAsJsonAsync("/shops", new { Name = "La boutique d'Alice" });
        await AssertStatusAsync(shopResponse, HttpStatusCode.Created);
        var shop = await shopResponse.Content.ReadFromJsonAsync<Shop>();

        var articleResponse = await _client.PostAsJsonAsync($"/shops/{shop!.Id}/articles", new
        {
            Title = "Figurine Star Wars originale",
            Description = "Figurine Hasbro vintage en excellent état, boîte incluse.",
            Price = 89.90m,
            ShippingCost = 5.90m,
            CategoryId = await SeedCategoryAsync(factory),
        });
        await AssertStatusAsync(articleResponse, HttpStatusCode.Created);
        var article = await articleResponse.Content.ReadFromJsonAsync<Article>();

        Assert.Equal(ArticleStatus.PendingReview, article!.Status);
        Assert.Contains(factory.EventBus.PublishedOf<ArticleSubmittedEvent>(), e => e.ArticleId == article.Id);

        // Tant que Moderation n'a pas statué (pas de RabbitMQ réel en test), l'article reste caché du catalogue public.
        var catalog = await _client.GetFromJsonAsync<List<Article>>("/articles");
        Assert.DoesNotContain(catalog!, a => a.Id == article.Id);
    }

    [Fact]
    public async Task Updating_the_price_of_an_article_publishes_PriceChangedEvent()
    {
        var shopResponse = await _client.PostAsJsonAsync("/shops", new { Name = "Autre boutique" });
        await AssertStatusAsync(shopResponse, HttpStatusCode.Created);
        var shop = await shopResponse.Content.ReadFromJsonAsync<Shop>();
        var articleResponse = await _client.PostAsJsonAsync($"/shops/{shop!.Id}/articles", new
        {
            Title = "Cassette V2000",
            Description = "Film culte en excellent état, testé et fonctionnel.",
            Price = 20m,
            ShippingCost = 3m,
            CategoryId = await SeedCategoryAsync(factory),
        });
        await AssertStatusAsync(articleResponse, HttpStatusCode.Created);
        var article = await articleResponse.Content.ReadFromJsonAsync<Article>();

        var priceResponse = await _client.PutAsJsonAsync($"/articles/{article!.Id}/price", new { NewPrice = 35m });

        await AssertStatusAsync(priceResponse, HttpStatusCode.OK);
        Assert.Contains(
            factory.EventBus.PublishedOf<PriceChangedEvent>(),
            e => e.ArticleId == article.Id && e.PreviousPrice == 20m && e.NewPrice == 35m);
    }

    [Fact]
    public async Task Creating_a_shop_without_a_token_is_rejected()
    {
        var anonymousClient = factory.CreateClient();

        var response = await anonymousClient.PostAsJsonAsync("/shops", new { Name = "Boutique anonyme" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_seller_cannot_change_the_price_of_another_sellers_article()
    {
        var shop = await (await _client.PostAsJsonAsync("/shops", new { Name = "Boutique d'Alice" })).Content.ReadFromJsonAsync<Shop>();
        var article = await (await _client.PostAsJsonAsync($"/shops/{shop!.Id}/articles", new
        {
            Title = "Baskets collector",
            Description = "Paire en édition limitée, jamais portée.",
            Price = 200m,
            ShippingCost = 8m,
            CategoryId = await SeedCategoryAsync(factory),
        })).Content.ReadFromJsonAsync<Article>();

        var intruder = CreateAuthenticatedClient(factory, out _);
        var response = await intruder.PutAsJsonAsync($"/articles/{article!.Id}/price", new { NewPrice = 1m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(factory.EventBus.PublishedOf<PriceChangedEvent>(), e => e.ArticleId == article.Id);
    }

    [Fact]
    public async Task A_seller_cannot_publish_an_article_in_another_sellers_shop()
    {
        var shop = await (await _client.PostAsJsonAsync("/shops", new { Name = "Boutique protégée" })).Content.ReadFromJsonAsync<Shop>();

        var intruder = CreateAuthenticatedClient(factory, out _);
        var response = await intruder.PostAsJsonAsync($"/shops/{shop!.Id}/articles", new
        {
            Title = "Faux article",
            Description = "Tentative d'injection dans la boutique d'un autre.",
            Price = 5m,
            ShippingCost = 1m,
            // Peu importe que cette catégorie existe : la vérification de propriété rejette avant.
            CategoryId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Creating_an_article_with_an_unknown_category_is_rejected()
    {
        Shop? shop = await (await _client.PostAsJsonAsync("/shops", new { Name = "Boutique" })).Content.ReadFromJsonAsync<Shop>();

        HttpResponseMessage response = await _client.PostAsJsonAsync($"/shops/{shop!.Id}/articles", new
        {
            Title = "Article orphelin",
            Description = "Catégorie inexistante.",
            Price = 10m,
            ShippingCost = 1m,
            CategoryId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
