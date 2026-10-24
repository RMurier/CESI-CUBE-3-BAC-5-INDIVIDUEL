namespace Collector.Contracts.Events;

/// <summary>Publié par Identity juste après l'inscription, consommé par Notification pour envoyer l'email de confirmation.</summary>
public sealed record EmailConfirmationRequestedEvent(
    Guid UserId,
    string Email,
    string ConfirmationToken,
    DateTimeOffset ExpiresAt);
