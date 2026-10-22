using Prometheus;
using Notification.Api.Contracts;
using Collector.Messaging;
using Collector.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Notification.Api.Domain;
using Notification.Api.Infrastructure;
using Notification.Api.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("omnia");
builder.Services.AddSingleton<IOmniaClient>(sp =>
    new OmniaClient(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IConfiguration>(), "Notification"));

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

// Log vers Omnia de toute exception non gérée par un endpoint (ceux-ci renvoient Results.X()
// directement pour leurs cas d'erreur métier — ce handler ne voit donc que les vrais bugs).
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async ctx =>
    {
        var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (ex is not null)
        {
            var omnia = ctx.RequestServices.GetRequiredService<IOmniaClient>();
            await omnia.SendLogAsync(
                category: "UnhandledException",
                level: "error",
                message: ex.Message,
                payload: new
                {
                    exceptionType = ex.GetType().FullName,
                    stackTrace = ex.StackTrace,
                    path = ctx.Request.Path.Value,
                    method = ctx.Request.Method,
                });
        }

        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Une erreur interne est survenue.",
            Detail = app.Environment.IsDevelopment() ? ex?.ToString() : null,
        });
    });
});

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
