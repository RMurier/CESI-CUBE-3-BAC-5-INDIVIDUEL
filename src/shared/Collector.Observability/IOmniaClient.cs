namespace Collector.Observability;

/// <summary>
/// Client vers Omnia (omnia-monitoring.com, outil propriétaire de l'utilisateur) — même contrat que
/// l'intégration déjà en place sur ses autres projets (ex. WealthFlowX). Les 5 services Collector
/// partagent un seul ProjectId Omnia (un seul secret Infisical pour tous) ; chaque implémentation
/// préfixe elle-même `category` par le nom du service pour les distinguer côté dashboard Omnia.
/// </summary>
public interface IOmniaClient
{
    Task SendLogAsync(string category, string level, string message, object? payload = null, CancellationToken ct = default);

    Task TrackLoginAsync(string userId, CancellationToken ct = default);
}
