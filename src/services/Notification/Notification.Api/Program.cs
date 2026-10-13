using Collector.Messaging;
using Microsoft.EntityFrameworkCore;
using Notification.Api.Domain;
using Notification.Api.Infrastructure;
using Notification.Api.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddCollectorEventBus(builder.Configuration);
builder.Services.AddHostedService<ArticlePublishedConsumer>();
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
    db.Database.EnsureCreated();
}

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

app.Run();

internal sealed record CreateSubscriptionRequest(Guid UserId, string Keyword);

public partial class Program;
