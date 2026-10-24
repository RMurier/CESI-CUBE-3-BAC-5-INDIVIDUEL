using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Catalog.Api.Domain;
using Collector.Testing;
using Xunit;

namespace Catalog.Api.Tests;

[Trait("Category", "Integration")]
public sealed class CategoryEndpointsTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private static HttpClient AdminClient(CatalogApiFactory f) =>
        AuthorizedClient(f, "Admin");

    private static HttpClient AuthorizedClient(CatalogApiFactory f, params string[] roles)
    {
        HttpClient client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.IssueToken(Guid.NewGuid(), roles));
        return client;
    }

    [Fact]
    public async Task Admin_creates_a_category_and_it_appears_in_the_public_list()
    {
        HttpClient admin = AdminClient(factory);
        string name = $"Posters {Guid.NewGuid()}";

        HttpResponseMessage response = await admin.PostAsJsonAsync("/categories", new { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        List<Category>? categories = await factory.CreateClient().GetFromJsonAsync<List<Category>>("/categories");
        Assert.Contains(categories!, c => c.Name == name);
    }

    [Fact]
    public async Task A_non_admin_cannot_create_a_category()
    {
        HttpClient seller = AuthorizedClient(factory, "Buyer", "Seller");

        HttpResponseMessage response = await seller.PostAsJsonAsync("/categories", new { Name = $"Interdit {Guid.NewGuid()}" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_category_with_a_duplicate_name_is_rejected()
    {
        HttpClient admin = AdminClient(factory);
        string name = $"Figurines {Guid.NewGuid()}";
        await admin.PostAsJsonAsync("/categories", new { Name = name });

        HttpResponseMessage second = await admin.PostAsJsonAsync("/categories", new { Name = name });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
