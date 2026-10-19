using System.Security.Cryptography;
using System.Text;
using Identity.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Api.Infrastructure;

public sealed class RefreshTokenService(IdentityDbContext db, IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    /// <summary>Crée un jeton, persiste son hash et renvoie la valeur en clair (affichée une seule fois).</summary>
    public async Task<string> IssueAsync(User user)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshExpiryDays),
        });
        await db.SaveChangesAsync();
        return raw;
    }

    /// <summary>
    /// Consomme un jeton (rotation). Si un jeton déjà révoqué est rejoué, c'est le signe d'un vol :
    /// toute la famille de jetons de l'utilisateur est révoquée.
    /// </summary>
    public async Task<User?> ConsumeAsync(string raw)
    {
        var hash = Hash(raw);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash);
        if (token is null) return null;

        var now = DateTimeOffset.UtcNow;
        if (token.RevokedAt is not null)
        {
            await RevokeAllAsync(token.UserId);
            return null;
        }
        if (token.ExpiresAt <= now) return null;

        token.RevokedAt = now;
        await db.SaveChangesAsync();
        return await db.Users.FindAsync(token.UserId);
    }

    public async Task RevokeAsync(string raw)
    {
        var hash = Hash(raw);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash);
        if (token is { RevokedAt: null })
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    private async Task RevokeAllAsync(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync();
        foreach (var t in active) t.RevokedAt = now;
        await db.SaveChangesAsync();
    }

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
