namespace Identity.Api.Domain;

/// <summary>
/// Jeton de rafraîchissement opaque. Seul son hash SHA-256 est persisté : une fuite de la base ne
/// permet pas de rejouer les jetons. Rotation à chaque usage (le jeton consommé est révoqué).
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid UserId { get; init; }
    public required string TokenHash { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
