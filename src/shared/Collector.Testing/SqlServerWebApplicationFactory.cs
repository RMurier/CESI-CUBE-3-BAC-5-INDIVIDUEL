using Collector.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;
using Xunit;

namespace Collector.Testing;

/// <summary>
/// Base de test d'intégration pour un service Collector : démarre un vrai SQL Server (Testcontainers),
/// remplace <see cref="IEventBus"/> par <see cref="FakeEventBus"/> (pas de RabbitMQ requis) et retire
/// les BackgroundService (consumers RabbitMQ) qui échoueraient sans broker.
/// </summary>
/// <typeparam name="TEntryPoint">Classe `Program` partielle du service testé.</typeparam>
/// <typeparam name="TDbContext">DbContext EF Core du service testé.</typeparam>
public abstract class SqlServerWebApplicationFactory<TEntryPoint, TDbContext>
    : WebApplicationFactory<TEntryPoint>, IAsyncLifetime
    where TEntryPoint : class
    where TDbContext : DbContext
{
    // Même image que celle utilisée en dev/uat/prod (infra/helm/collector/values.yaml -> sqlServer.image).
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public FakeEventBus EventBus { get; } = new();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _sqlContainer.StartAsync();

        // Lu par WebApplication.CreateBuilder(args) au tout début de Program.cs (AddEnvironmentVariables
        // fait partie des sources de config par défaut) — doit donc être positionné AVANT que le host
        // lazy de WebApplicationFactory ne soit construit (donc avant le premier CreateClient()/Services).
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _sqlContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__SigningKey", TestJwtTokenFactory.SigningKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestJwtTokenFactory.Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestJwtTokenFactory.Audience);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _sqlContainer.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Appartient après l'AddDbContext de Program.cs : on force la vraie chaîne de connexion
            // du conteneur de test, indépendamment de la config (voir RemoveAll + re-registration).
            services.RemoveAll<DbContextOptions<TDbContext>>();
            services.AddDbContext<TDbContext>(options => options.UseSqlServer(_sqlContainer.GetConnectionString()));

            // Pas de RabbitMQ réel en test d'intégration : double en mémoire + suppression des
            // BackgroundService consumers (qui échoueraient en boucle sans broker disponible).
            services.RemoveAll<IEventBus>();
            services.AddSingleton<IEventBus>(EventBus);
            services.RemoveAll<IHostedService>();
        });
    }
}
