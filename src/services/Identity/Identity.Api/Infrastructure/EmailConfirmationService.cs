using System.Security.Cryptography;
using System.Text;
using Identity.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Identity.Api.Infrastructure;

public sealed class EmailConfirmationService(IdentityDbContext db)
{
    /// <summary>Fenêtre raisonnable pour cliquer le lien reçu par email avant d'en redemander un.</summary>
    private static readonly TimeSpan Expiry = TimeSpan.FromHours(48);

    /// <summary>Crée un jeton, persiste son hash et renvoie la valeur en clair (à inclure dans l'email) + son expiration.</summary>
    public async Task<(string Token, DateTimeOffset ExpiresAt)> IssueAsync(User user)
    {
        string raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.Add(Expiry);

        db.ConfirmationTokens.Add(new ConfirmationToken
        {
            UserId = user.Id,
            TokenHash = Hash(raw),
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
        return (raw, expiresAt);
    }

    /// <summary>Valide le jeton et marque l'utilisateur comme confirmé. Idempotent : un lien déjà utilisé reste accepté.</summary>
    public async Task<bool> ConfirmAsync(string raw)
    {
        string hash = Hash(raw);
        ConfirmationToken? token = await db.ConfirmationTokens.SingleOrDefaultAsync(t => t.TokenHash == hash);
        if (token is null) return false;
        if (token.ConfirmedAt is not null) return true;
        if (token.ExpiresAt <= DateTimeOffset.UtcNow) return false;

        User? user = await db.Users.FindAsync(token.UserId);
        if (user is null) return false;

        token.ConfirmedAt = DateTimeOffset.UtcNow;
        user.IsEmailConfirmed = true;
        await db.SaveChangesAsync();
        return true;
    }

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
