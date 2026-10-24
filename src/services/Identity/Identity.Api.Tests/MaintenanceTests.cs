using System.Net;
using System.Net.Http.Json;
using Identity.Api.Domain;
using Identity.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identity.Api.Tests;

[Trait("Category", "Integration")]
public sealed class MaintenanceTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Key = "test-maintenance-key";

    private HttpClient ClientWithKey(string? key)
    {
        var client = factory
            .WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
                c.AddInMemoryCollection(new Dictionary<string, string?> { ["Maintenance:ApiKey"] = Key })))
            .CreateClient();
        if (key is not null) client.DefaultRequestHeaders.Add("X-Maintenance-Key", key);
        return client;
    }

    [Fact]
    public async Task Purge_is_disabled_without_a_configured_key()
    {
        var response = await factory.CreateClient().PostAsync("/internal/maintenance/purge-refresh-tokens", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Purge_rejects_a_missing_or_wrong_key(string? key)
    {
        var response = await ClientWithKey(key).PostAsync("/internal/maintenance/purge-refresh-tokens", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Purge_removes_expired_tokens_and_keeps_active_ones()
    {
        var userId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.AddRange(
                new RefreshToken { UserId = userId, TokenHash = "expired", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) },
                new RefreshToken { UserId = userId, TokenHash = "active", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }

        var response = await ClientWithKey(Key).PostAsync("/internal/maintenance/purge-refresh-tokens", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(verifyDb.RefreshTokens.Any(t => t.TokenHash == "expired"));
        Assert.True(verifyDb.RefreshTokens.Any(t => t.TokenHash == "active"));
    }

    [Fact]
    public async Task Confirmation_token_purge_removes_expired_tokens_and_keeps_active_ones()
    {
        Guid userId = Guid.NewGuid();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.ConfirmationTokens.AddRange(
                new ConfirmationToken { UserId = userId, TokenHash = "expired", ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) },
                new ConfirmationToken { UserId = userId, TokenHash = "active", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            await db.SaveChangesAsync();
        }

        HttpResponseMessage response = await ClientWithKey(Key).PostAsync("/internal/maintenance/purge-confirmation-tokens", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using IServiceScope verify = factory.Services.CreateScope();
        IdentityDbContext verifyDb = verify.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(verifyDb.ConfirmationTokens.Any(t => t.TokenHash == "expired"));
        Assert.True(verifyDb.ConfirmationTokens.Any(t => t.TokenHash == "active"));
    }
}
