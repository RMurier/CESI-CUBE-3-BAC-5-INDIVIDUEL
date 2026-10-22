using Prometheus;
using Identity.Api.Contracts;
using System.Security.Claims;
using System.Text;
using Identity.Api.Domain;
using Identity.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Collector.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("omnia");
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IOmniaClient>(sp =>
    new OmniaClient(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IConfiguration>(), "Identity"));

// Endpoint /metrics sur un port dédié (jamais exposé par Traefik, scrapé par Prometheus via l'IP du pod).
// Désactivé si Metrics:Port est absent (tests d'intégration : plusieurs hôtes dans le même processus).
var metricsPort = builder.Configuration.GetValue<int?>("Metrics:Port");
if (metricsPort is not null)
{
    builder.Services.AddMetricServer(options => options.Port = (ushort)metricsPort.Value);
}

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<RefreshTokenService>();

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Configuration 'Jwt' manquante.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Sans ça, JwtSecurityTokenHandler remappe "sub" vers l'URI long historique
        // (ClaimTypes.NameIdentifier) à la lecture du token : FindFirstValue(JwtRegisteredClaimNames.Sub)
        // renvoie alors toujours null, et Guid.Parse(null) lève une ArgumentNullException (500) sur
        // tout endpoint qui lit l'identité de l'appelant (ex: /auth/become-seller).
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
        };
    });
builder.Services.AddAuthorization();

var frontendOrigin = builder.Configuration["Cors:FrontendOrigin"]
    ?? throw new InvalidOperationException("Configuration 'Cors:FrontendOrigin' manquante.");
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins(frontendOrigin)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Derrière Traefik, l'IP du client arrive dans X-Forwarded-For : sans ça le rate limiter verrait
// une seule IP (celle du proxy) et bloquerait tout le monde d'un coup.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Anti brute-force / credential stuffing : fenêtre fixe par IP sur les endpoints d'authentification.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Config lue à la requête (et non au démarrage) pour pouvoir être surchargée par les tests.
    options.AddPolicy("auth", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimiting:AuthPermitLimit", 10),
                Window = TimeSpan.FromSeconds(config.GetValue("RateLimiting:AuthWindowSeconds", 60)),
                QueueLimit = 0,
            });
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<IdentityDbContext>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // Prototype : EnsureCreated plutôt que des migrations formelles (cf. README section 11).
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await db.EnsureCreatedAndUpToDateAsync();
}

app.UseCorrelationId();
app.UseOmniaExceptionLogging(app.Environment.IsDevelopment());

app.UseHttpMetrics();
app.UseForwardedHeaders();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

app.MapPost("/auth/register", async (RegisterRequest request, IdentityDbContext db) =>
{
    if (await db.Users.AnyAsync(u => u.Email == request.Email))
    {
        return Results.Conflict(new { message = "Un compte existe déjà avec cet email." });
    }

    var user = new User
    {
        Email = request.Email,
        DisplayName = request.DisplayName,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/users/{user.Id}", new { user.Id, user.Email, user.DisplayName });
}).RequireRateLimiting("auth");

app.MapPost("/auth/login", async (LoginRequest request, IdentityDbContext db, JwtTokenService tokenService, RefreshTokenService refreshTokens, IOmniaClient omnia) =>
{
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == request.Email);
    if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
    {
        return Results.Unauthorized();
    }

    var token = tokenService.IssueToken(user);
    var refreshToken = await refreshTokens.IssueAsync(user);
    await omnia.TrackLoginAsync(user.Id.ToString());
    return Results.Ok(new { accessToken = token, refreshToken, roles = user.Roles() });
}).RequireRateLimiting("auth");

app.MapPost("/auth/refresh", async (RefreshRequest request, JwtTokenService tokenService, RefreshTokenService refreshTokens) =>
{
    var user = await refreshTokens.ConsumeAsync(request.RefreshToken);
    if (user is null) return Results.Unauthorized();

    var token = tokenService.IssueToken(user);
    var refreshToken = await refreshTokens.IssueAsync(user);
    return Results.Ok(new { accessToken = token, refreshToken, roles = user.Roles() });
}).RequireRateLimiting("auth");

app.MapPost("/auth/logout", async (RefreshRequest request, RefreshTokenService refreshTokens) =>
{
    await refreshTokens.RevokeAsync(request.RefreshToken);
    return Results.NoContent();
});

app.MapPost("/auth/become-seller", async (ClaimsPrincipal principal, IdentityDbContext db) =>
{
    var userId = Guid.Parse(principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);
    var user = await db.Users.FindAsync(userId);
    if (user is null) return Results.NotFound();

    user.IsSeller = true;
    await db.SaveChangesAsync();
    return Results.Ok(new { user.Id, roles = user.Roles() });
}).RequireAuthorization();

// Pas de gateway dédié parmi les 5 services : le frontend remonte ses erreurs ici (Identity est
// le service que toute session, connectée ou pas, touche déjà pour l'auth).
app.MapPost("/logs/client-error", async (ClientErrorRequest request, IOmniaClient omnia) =>
{
    await omnia.SendLogAsync(
        category: "FrontendError",
        level: "error",
        message: request.Message,
        // category est préfixée par le nom du service qui relaie l'appel ("Identity.FrontendError"),
        // pas par l'origine réelle de l'erreur : on la précise explicitement pour ne pas laisser croire
        // que c'est Identity qui a planté alors que c'est le navigateur.
        payload: new { service = "Frontend", source = request.Source, stackTrace = request.StackTrace, url = request.Url });
    return Results.NoContent();
}).AllowAnonymous();

// Déclenche volontairement une exception non gérée, pour valider de bout en bout l'envoi vers Omnia
// (UseExceptionHandler -> IOmniaClient -> dashboard) sans attendre un vrai bug. Conservé en
// permanence (utile en démo) : le nom d'environnement (Development/Staging/Production, cf.
// configmap.yaml) et un jeton aléatoire sont inclus dans le message pour distinguer dans le
// dashboard Omnia les appels faits depuis dev/uat/prod.
app.MapGet("/debug/omnia-test", (IWebHostEnvironment env) =>
{
    var token = Random.Shared.Next(1000, 9999);
    throw new InvalidOperationException($"Test manuel de l'intégration Omnia [{env.EnvironmentName}] #{token}.");
}).AllowAnonymous();

app.MapMaintenanceEndpoints();

await app.RunAsync();


/// <summary>Exposé pour les tests d'intégration (WebApplicationFactory).</summary>
public partial class Program;
