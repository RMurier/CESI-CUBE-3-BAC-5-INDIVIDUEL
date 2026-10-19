using Prometheus;
using Notification.Api.Contracts;
using Collector.Messaging;
using Microsoft.EntityFrameworkCore;
using Notification.Api.Domain;
using Notification.Api.Infrastructure;
using Notification.Api.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Endpoint /metrics sur un port dédié (jamais exposé par Traefik, scrapé par Prometheus via l'IP du pod).
// Désactivé si Metrics:Port est absent (tests d'intégration : plusieurs hôtes dans le même processus).
var metricsPort = builder.Configuration.GetValue<int?>("Metrics:Port");
if (metricsPort is not null)
{
    builder.Services.AddMetricServer(options => options.Port = (ushort)metricsPort.Value);
}

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddCollectorEventBus(builder.Configuration);
builder.Services.AddHostedService<ArticlePublishedConsumer>();
builder.Services.AddHostedService<OrderPaidConsumer>();
builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();

var frontendOrigin = builder.Configuration["Cors:FrontendOrigin"]
    ?? throw new InvalidOperationException("Configuration 'Cors:FrontendOrigin' manquante.");
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins(frontendOrigin)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<NotificationDbContext>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseHttpMetrics();
app.UseCors("Frontend");

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

app.MapPost("/subscriptions", async (CreateSubscriptionRequest request, NotificationDbContext db) =>
{
    var subscription = new InterestSubscription { UserId = request.UserId, Keyword = request.Keyword };
    db.Subscriptions.Add(subscription);
    await db.SaveChangesAsync();
    return Results.Created($"/subscriptions/{subscription.Id}", subscription);
});

app.MapGet("/notifications/{userId:guid}", async (Guid userId, NotificationDbContext db) =>
    await db.Notifications
        .Where(n => n.UserId == userId)
        .OrderByDescending(n => n.CreatedAt)
        .ToListAsync());

app.MapPost("/notifications/{id:guid}/read", async (Guid id, NotificationDbContext db) =>
{
    var notification = await db.Notifications.FindAsync(id);
    if (notification is null) return Results.NotFound();

    notification.Read = true;
    await db.SaveChangesAsync();
    return Results.NoContent();
});

await app.RunAsync();


public partial class Program;
