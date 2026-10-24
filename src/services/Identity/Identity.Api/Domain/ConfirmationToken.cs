namespace Identity.Api.Domain;

/// <summary>
/// Jeton opaque de confirmation d'email, même protocole que <see cref="RefreshToken"/> (seul le hash
/// SHA-256 est persisté). Durée de vie courte (voir EmailConfirmationService) ; purgé par le DAG
/// Airflow `collector_purge_confirmation_tokens` une fois expiré, confirmé ou non.
/// </summary>
public sealed class ConfirmationToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid UserId { get; init; }
    public required string TokenHash { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConfirmedAt { get; set; }
}
