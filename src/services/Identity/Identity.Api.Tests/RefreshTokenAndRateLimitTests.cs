using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Identity.Api.Tests;

[Trait("Category", "Integration")]
public sealed class RefreshTokenAndRateLimitTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Password = "Sup3r-Secret!";

    private static async Task<TokenResponse> RegisterAndLoginAsync(HttpClient client)
    {
        var email = $"{Guid.NewGuid()}@collector.shop";
        await client.PostAsJsonAsync("/auth/register", new { Email = email, Password, DisplayName = "Test" });
        var login = await client.PostAsJsonAsync("/auth/login", new { Email = email, Password });
        return (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_the_old_one_stops_working()
    {
        var client = factory.CreateClient();
        var first = await RegisterAndLoginAsync(client);

        var refreshed = await client.PostAsJsonAsync("/auth/refresh", new { first.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        var replay = await client.PostAsJsonAsync("/auth/refresh", new { first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Replaying_a_revoked_refresh_token_revokes_the_whole_family()
    {
        var client = factory.CreateClient();
        var first = await RegisterAndLoginAsync(client);
        var second = (await (await client.PostAsJsonAsync("/auth/refresh", new { first.RefreshToken }))
            .Content.ReadFromJsonAsync<TokenResponse>())!;

        await client.PostAsJsonAsync("/auth/refresh", new { first.RefreshToken }); // rejeu = vol présumé

        var legit = await client.PostAsJsonAsync("/auth/refresh", new { second.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, legit.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var client = factory.CreateClient();
        var tokens = await RegisterAndLoginAsync(client);

        var logout = await client.PostAsJsonAsync("/auth/logout", new { tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await client.PostAsJsonAsync("/auth/refresh", new { tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Login_is_rate_limited_after_too_many_attempts()
    {
        var limited = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:AuthPermitLimit"] = "3" })));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync("/auth/login", new { Email = "nobody@collector.shop", Password = "x" });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    private sealed record TokenResponse(string AccessToken, string RefreshToken, string[] Roles);
}
