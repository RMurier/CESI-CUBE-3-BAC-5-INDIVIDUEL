using Prometheus;
using System.Text;
using Chat.Api.Hubs;
using Chat.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Endpoint /metrics sur un port dédié (jamais exposé par Traefik, scrapé par Prometheus via l'IP du pod).
// Désactivé si Metrics:Port est absent (tests d'intégration : plusieurs hôtes dans le même processus).
var metricsPort = builder.Configuration.GetValue<int?>("Metrics:Port");
if (metricsPort is not null)
{
    builder.Services.AddMetricServer(options => options.Port = (ushort)metricsPort.Value);
}

builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSignalR();

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Configuration 'Jwt:SigningKey' manquante.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "collector-identity";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "collector-api";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Sans ça, JwtSecurityTokenHandler remappe "sub" vers l'URI long historique
        // (ClaimTypes.NameIdentifier) à la lecture du token : FindFirstValue(JwtRegisteredClaimNames.Sub)
        // renvoie alors toujours null, et Guid.Parse(null) lève une ArgumentNullException dans ChatHub.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
        };

        // SignalR sur WebSocket ne peut pas poser de header Authorization : le token est passé en
        // query string (convention officielle ASP.NET Core SignalR).
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/chat"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ChatDbContext>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseHttpMetrics();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

app.MapHub<ChatHub>("/hubs/chat");

app.MapGet("/conversations/{conversationId}/messages", async (string conversationId, ChatDbContext db) =>
    await db.Messages
        .Where(m => m.ConversationId == conversationId)
        .OrderBy(m => m.SentAt)
        .ToListAsync())
    .RequireAuthorization();

await app.RunAsync();

public partial class Program;
