using Collector.Testing;
using Identity.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Identity.Api.Tests;

public sealed class IdentityApiFactory : SqlServerWebApplicationFactory<Program, IdentityDbContext>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Limite très haute par défaut : les tests partagent la même IP (loopback) et enchaînent
        // beaucoup d'appels d'auth. Le test de rate limiting la rabaisse via WithWebHostBuilder.
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:AuthPermitLimit"] = "1000",
        }));
    }
}
