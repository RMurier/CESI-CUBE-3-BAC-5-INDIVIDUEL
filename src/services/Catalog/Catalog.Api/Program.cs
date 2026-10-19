using Prometheus;
using Catalog.Api.Contracts;
using System.Security.Claims;
using Catalog.Api.Domain;
using Catalog.Api.Endpoints;
using Catalog.Api.Payments;
using Catalog.Api.Infrastructure;
using Catalog.Api.Messaging;
using Collector.Contracts.Events;
using Collector.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Endpoint /metrics sur un port dédié (jamais exposé par Traefik, scrapé par Prometheus via l'IP du pod).
// Désactivé si Metrics:Port est absent (tests d'intégration : plusieurs hôtes dans le même processus).
var metricsPort = builder.Configuration.GetValue<int?>("Metrics:Port");
if (metricsPort is not null)
{
    builder.Services.AddMetricServer(options => options.Port = (ushort)metricsPort.Value);
}

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddCollectorEventBus(builder.Configuration);

builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.SectionName));
if (string.IsNullOrWhiteSpace(builder.Configuration["Stripe:SecretKey"]))
{
    builder.Services.AddSingleton<IPaymentGateway, DisabledPaymentGateway>();
}
else
{
    builder.Services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
}
builder.Services.AddHostedService<ModerationResultConsumer>();

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
        // renvoie alors toujours null, et Guid.Parse(null) lève une ArgumentNullException (500) sur
        // tout endpoint qui lit l'identité de l'appelant.
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
    });
builder.Services.AddAuthorization();

var frontendOrigin = builder.Configuration["Cors:FrontendOrigin"]
    ?? throw new InvalidOperationException("Configuration 'Cors:FrontendOrigin' manquante.");
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins(frontendOrigin)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<CatalogDbContext>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.EnsureCreatedAndUpToDateAsync();
}

app.UseHttpMetrics();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

// --- Boutiques ---

app.MapPost("/shops", async (CreateShopRequest request, ClaimsPrincipal principal, CatalogDbContext db) =>
{
    var sellerId = Guid.Parse(principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);
    var shop = new Shop { SellerId = sellerId, Name = request.Name, Description = request.Description };
    db.Shops.Add(shop);
    await db.SaveChangesAsync();
    return Results.Created($"/shops/{shop.Id}", shop);
}).RequireAuthorization();

app.MapGet("/shops/{id:guid}", async (Guid id, CatalogDbContext db) =>
    await db.Shops.Include(s => s.Articles).SingleOrDefaultAsync(s => s.Id == id)
        is { } shop ? Results.Ok(shop) : Results.NotFound());

// --- Articles ---

app.MapPost("/shops/{shopId:guid}/articles", async (
    Guid shopId,
    CreateArticleRequest request,
    ClaimsPrincipal principal,
    CatalogDbContext db,
    IEventBus eventBus) =>
{
    var shop = await db.Shops.FindAsync(shopId);
    if (shop is null) return Results.NotFound();
    if (shop.SellerId != CurrentUserId(principal)) return Results.Forbid();

    var article = new Article
    {
        ShopId = shopId,
        Title = request.Title,
        Description = request.Description,
        Price = request.Price,
        ShippingCost = request.ShippingCost,
    };
    db.Articles.Add(article);
    await db.SaveChangesAsync();

    await eventBus.PublishAsync(new ArticleSubmittedEvent(
        article.Id, shopId, shop.SellerId, article.Title, article.Description,
        article.Price, article.ShippingCost, article.SubmittedAt));

    return Results.Created($"/articles/{article.Id}", article);
}).RequireAuthorization();

app.MapGet("/articles", async (CatalogDbContext db) =>
    await db.Articles.Where(a => a.Status == ArticleStatus.Published).ToListAsync());

app.MapGet("/articles/{id:guid}", async (Guid id, CatalogDbContext db) =>
    await db.Articles.FindAsync(id) is { } article ? Results.Ok(article) : Results.NotFound());

app.MapPut("/articles/{id:guid}/price", async (
    Guid id,
    UpdatePriceRequest request,
    ClaimsPrincipal principal,
    CatalogDbContext db,
    IEventBus eventBus) =>
{
    var article = await db.Articles.FindAsync(id);
    if (article is null) return Results.NotFound();

    var owner = await db.Shops.Where(sh => sh.Id == article.ShopId).Select(sh => sh.SellerId).SingleAsync();
    if (owner != CurrentUserId(principal)) return Results.Forbid();

    var previousPrice = article.Price;
    article.Price = request.NewPrice;
    await db.SaveChangesAsync();

    await eventBus.PublishAsync(new PriceChangedEvent(article.Id, previousPrice, article.Price, DateTimeOffset.UtcNow));

    return Results.Ok(article);
}).RequireAuthorization();

app.MapOrderEndpoints();
app.MapPayoutEndpoints();
app.MapMaintenanceEndpoints();

await app.RunAsync();

static Guid CurrentUserId(ClaimsPrincipal principal) =>
    Guid.Parse(principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);


public partial class Program;
