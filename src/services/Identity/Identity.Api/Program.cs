using System.Security.Claims;
using System.Text;
using Identity.Api.Domain;
using Identity.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Configuration 'Jwt' manquante.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
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

builder.Services.AddHealthChecks()
    .AddDbContextCheck<IdentityDbContext>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // Prototype : EnsureCreated plutôt que des migrations formelles (cf. README section 11).
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    db.Database.EnsureCreated();
}

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
});

app.MapPost("/auth/login", async (LoginRequest request, IdentityDbContext db, JwtTokenService tokenService) =>
{
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == request.Email);
    if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
    {
        return Results.Unauthorized();
    }

    var token = tokenService.IssueToken(user);
    return Results.Ok(new { accessToken = token, roles = user.Roles() });
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

app.Run();

internal sealed record RegisterRequest(string Email, string Password, string DisplayName);
internal sealed record LoginRequest(string Email, string Password);

/// <summary>Exposé pour les tests d'intégration (WebApplicationFactory).</summary>
public partial class Program;
