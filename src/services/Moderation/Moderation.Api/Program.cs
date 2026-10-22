using Prometheus;
using Collector.Messaging;
using Collector.Observability;
using Microsoft.EntityFrameworkCore;
using Moderation.Api.Domain;
using Moderation.Api.Infrastructure;
using Moderation.Api.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("omnia");
builder.Services.AddSingleton<IOmniaClient>(sp =>
    new OmniaClient(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IConfiguration>(), "Moderation"));

// Endpoint /metrics sur un port dédié (jamais exposé par Traefik, scrapé par Prometheus via l'IP du pod).
// Désactivé si Metrics:Port est absent (tests d'intégration : plusieurs hôtes dans le même processus).
var metricsPort = builder.Configuration.GetValue<int?>("Metrics:Port");
if (metricsPort is not null)
{
    builder.Services.AddMetricServer(options => options.Port = (ushort)metricsPort.Value);
}

builder.Services.AddDbContext<ModerationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddCollectorEventBus(builder.Configuration);
builder.Services.AddSingleton<IFraudDetector, RuleBasedFraudDetector>();
builder.Services.AddHostedService<ArticleSubmittedConsumer>();
builder.Services.AddHostedService<PriceChangedConsumer>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ModerationDbContext>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseOmniaExceptionLogging(app.Environment.IsDevelopment());

app.UseHttpMetrics();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

// Endpoint d'audit simple pour consultation back-office (pas d'écriture directe : tout passe par RabbitMQ).
app.MapGet("/decisions/{articleId:guid}", async (Guid articleId, ModerationDbContext db) =>
    await db.Decisions.Where(d => d.ArticleId == articleId).OrderByDescending(d => d.EvaluatedAt).ToListAsync());

await app.RunAsync();

public partial class Program;
