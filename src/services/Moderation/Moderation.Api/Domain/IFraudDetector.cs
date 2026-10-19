using Collector.Contracts.Events;

namespace Moderation.Api.Domain;

public sealed record FraudVerdict(bool Suspicious, string? Reason);

/// <summary>
/// Point d'extension "build vs buy" demandé par la direction : le service ne dépend que de cette
/// interface. Brancher un outil de détection externe = ajouter une implémentation (appel HTTP vers
/// l'éditeur, scoring ML...) et changer l'enregistrement DI dans Program.cs, sans toucher aux consumers.
/// </summary>
public interface IFraudDetector
{
    FraudVerdict AssessPriceChange(PriceChangedEvent change);
}

/// <summary>Implémentation "build" par défaut : heuristique de variation de prix.</summary>
public sealed class RuleBasedFraudDetector : IFraudDetector
{
    public const double SuspiciousVariationThreshold = 0.5;

    public FraudVerdict AssessPriceChange(PriceChangedEvent change)
    {
        var variation = change.PreviousPrice == 0
            ? 1.0
            : (double)Math.Abs(change.NewPrice - change.PreviousPrice) / (double)change.PreviousPrice;

        return variation > SuspiciousVariationThreshold
            ? new FraudVerdict(true, $"Variation de prix suspecte : {variation:P0} ({change.PreviousPrice:C} → {change.NewPrice:C})")
            : new FraudVerdict(false, null);
    }
}
