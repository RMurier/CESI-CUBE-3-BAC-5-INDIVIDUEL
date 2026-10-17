using Collector.Messaging;
using Microsoft.EntityFrameworkCore;
using Moderation.Api.Infrastructure;
using Moderation.Api.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ModerationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddCollectorEventBus(builder.Configuration);
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

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

// Endpoint d'audit simple pour consultation back-office (pas d'écriture directe : tout passe par RabbitMQ).
app.MapGet("/decisions/{articleId:guid}", async (Guid articleId, ModerationDbContext db) =>
    await db.Decisions.Where(d => d.ArticleId == articleId).OrderByDescending(d => d.EvaluatedAt).ToListAsync());

await app.RunAsync();

public partial class Program;
