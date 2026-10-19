using Catalog.Api.Infrastructure;
using Catalog.Api.Payments;
using Collector.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catalog.Api.Tests;

public sealed class CatalogApiFactory : SqlServerWebApplicationFactory<Program, CatalogDbContext>
{
    public FakePaymentGateway Payments { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(Payments);
        });
    }
}
