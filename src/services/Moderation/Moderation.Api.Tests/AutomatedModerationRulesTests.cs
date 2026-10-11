using Collector.Contracts.Events;
using Moderation.Api.Domain;
using Xunit;

namespace Moderation.Api.Tests;

public sealed class AutomatedModerationRulesTests
{
    private static ArticleSubmittedEvent ValidArticle(decimal price = 49.90m, string description = "Figurine Hasbro vintage en excellent état.") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Figurine Star Wars", description, price, 4.90m, DateTimeOffset.UtcNow);

    [Fact]
    public void A_well_formed_article_is_approved()
    {
        var (approved, reason) = AutomatedModerationRules.Evaluate(ValidArticle());

        Assert.True(approved);
        Assert.Null(reason);
    }

    [Fact]
    public void An_article_with_a_zero_price_is_rejected()
    {
        var (approved, reason) = AutomatedModerationRules.Evaluate(ValidArticle(price: 0m));

        Assert.False(approved);
        Assert.Contains("Prix invalide", reason);
    }

    [Fact]
    public void An_article_with_a_suspiciously_high_price_is_rejected()
    {
        var (approved, reason) = AutomatedModerationRules.Evaluate(ValidArticle(price: 100_000m));

        Assert.False(approved);
        Assert.Contains("Prix suspect", reason);
    }

    [Fact]
    public void An_article_with_a_too_short_description_is_rejected()
    {
        var (approved, reason) = AutomatedModerationRules.Evaluate(ValidArticle(description: "Top"));

        Assert.False(approved);
        Assert.Contains("Description trop courte", reason);
    }
}
