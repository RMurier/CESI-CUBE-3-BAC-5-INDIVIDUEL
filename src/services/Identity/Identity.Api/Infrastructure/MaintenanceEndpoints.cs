using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Identity.Api.Infrastructure;

/// <summary>
/// Traitements récurrents pilotés par Airflow (voir infra/airflow/dags). Protégés par une clé partagée
/// (en-tête X-Maintenance-Key, secret Infisical Maintenance__ApiKey) ; absents (404) si la clé n'est pas configurée.
/// </summary>
public static class MaintenanceEndpoints
{
    /// <summary>Durée de conservation d'un jeton révoqué : on garde l'historique assez longtemps pour détecter les rejeux.</summary>
    private static readonly TimeSpan RevokedRetention = TimeSpan.FromDays(30);

    public static void MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal").AddEndpointFilter(RequireMaintenanceKey);

        // Purge des refresh tokens expirés ou révoqués depuis longtemps (la table grossit à chaque login/refresh).
        group.MapPost("/maintenance/purge-refresh-tokens", async (IdentityDbContext db, CancellationToken cancellationToken) =>
        {
            var now = DateTimeOffset.UtcNow;
            var revokedBefore = now - RevokedRetention;
            var deleted = await db.RefreshTokens
                .Where(t => t.ExpiresAt < now || (t.RevokedAt != null && t.RevokedAt < revokedBefore))
                .ExecuteDeleteAsync(cancellationToken);
            return Results.Ok(new { deleted });
        });

        // Résolution userId -> email, appelée par Notification.Api pour l'envoi SMTP (voir SmtpEmailSender).
        group.MapGet("/users/{id:guid}/email", async (Guid id, IdentityDbContext db, CancellationToken cancellationToken) =>
        {
            var email = await db.Users
                .Where(u => u.Id == id)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);
            return email is null ? Results.NotFound() : Results.Ok(new { email });
        });
    }

    private static async ValueTask<object?> RequireMaintenanceKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Maintenance:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected)) return Results.NotFound();

        var provided = context.HttpContext.Request.Headers["X-Maintenance-Key"].ToString();
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
        return ok ? await next(context) : Results.Unauthorized();
    }
}
