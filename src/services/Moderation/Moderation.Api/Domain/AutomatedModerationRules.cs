using Collector.Contracts.Events;

namespace Moderation.Api.Domain;

/// <summary>
/// Règles de contrôle automatisé appliquées avant mise en ligne d'un article (exigence contexte
/// Collector : "le contrôle doit pouvoir être automatisé le plus possible"). Heuristiques simples
/// pour le POC — à terme, point d'extension pour un outil de détection de fraude externe (build/buy
/// non tranché par la direction, cf. README).
/// </summary>
public static class AutomatedModerationRules
{
    private const decimal MaxReasonablePrice = 50_000m;
    private const int MinDescriptionLength = 10;

    public static (bool Approved, string? Reason) Evaluate(ArticleSubmittedEvent article)
    {
        if (string.IsNullOrWhiteSpace(article.Title))
        {
            return (false, "Titre manquant.");
        }

        if (article.Description.Length < MinDescriptionLength)
        {
            return (false, $"Description trop courte (minimum {MinDescriptionLength} caractères).");
        }

        if (article.Price <= 0)
        {
            return (false, "Prix invalide (doit être strictement positif).");
        }

        if (article.Price > MaxReasonablePrice)
        {
            return (false, $"Prix suspect : supérieur au seuil de {MaxReasonablePrice:C}, nécessite une revue manuelle.");
        }

        return (true, null);
    }
}
