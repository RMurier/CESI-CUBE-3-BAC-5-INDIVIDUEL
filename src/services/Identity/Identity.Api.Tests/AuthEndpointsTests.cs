using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Identity.Api.Tests;

[Trait("Category", "Integration")]
public sealed class AuthEndpointsTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Register_then_login_returns_an_access_token()
    {
        var email = $"{Guid.NewGuid()}@collector.shop";

        var registerResponse = await _client.PostAsJsonAsync("/auth/register", new
        {
            Email = email,
            Password = "Sup3r-Secret!",
            DisplayName = "Alice Acheteuse",
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new
        {
            Email = email,
            Password = "Sup3r-Secret!",
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var payload = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(payload?.AccessToken));
        Assert.Contains("Buyer", payload!.Roles);
    }

    [Fact]
    public async Task Register_with_an_already_used_email_returns_conflict()
    {
        var email = $"{Guid.NewGuid()}@collector.shop";
        var request = new { Email = email, Password = "Sup3r-Secret!", DisplayName = "Bob Vendeur" };

        await _client.PostAsJsonAsync("/auth/register", request);
        var secondAttempt = await _client.PostAsJsonAsync("/auth/register", request);

        Assert.Equal(HttpStatusCode.Conflict, secondAttempt.StatusCode);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_returns_unauthorized()
    {
        var email = $"{Guid.NewGuid()}@collector.shop";
        await _client.PostAsJsonAsync("/auth/register", new
        {
            Email = email,
            Password = "Correct-Password!",
            DisplayName = "Carla Collector",
        });

        var response = await _client.PostAsJsonAsync("/auth/login", new { Email = email, Password = "Wrong-Password!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record LoginResponse(string AccessToken, string[] Roles);
}
