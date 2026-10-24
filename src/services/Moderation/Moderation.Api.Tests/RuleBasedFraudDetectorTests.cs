using Collector.Contracts.Events;
using Moderation.Api.Domain;
using Xunit;

namespace Moderation.Api.Tests;

[Trait("Category", "Unit")]
public sealed class RuleBasedFraudDetectorTests
{
    private readonly RuleBasedFraudDetector _detector = new();

    private static PriceChangedEvent Change(decimal from, decimal to) =>
        new(Guid.NewGuid(), "Article de test", from, to, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(100, 120)] // +20 %
    [InlineData(100, 150)] // +50 % pile : seuil exclusif
    [InlineData(100, 60)]  // -40 %
    public void Moderate_variations_are_not_suspicious(decimal from, decimal to)
    {
        Assert.False(_detector.AssessPriceChange(Change(from, to)).Suspicious);
    }

    [Theory]
    [InlineData(100, 151)]
    [InlineData(100, 10)]
    [InlineData(0, 25)]
    public void Large_variations_are_flagged_with_a_reason(decimal from, decimal to)
    {
        var verdict = _detector.AssessPriceChange(Change(from, to));

        Assert.True(verdict.Suspicious);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));
    }
}
