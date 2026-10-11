using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moderation.Api.Domain;
using Moderation.Api.Infrastructure;
using Xunit;

namespace Moderation.Api.Tests;

public sealed class DecisionsEndpointTests(ModerationApiFactory factory) : IClassFixture<ModerationApiFactory>
{
    [Fact]
    public async Task Decisions_for_an_article_are_returned_most_recent_first()
    {
        var articleId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();
            db.Decisions.AddRange(
                new ModerationDecision { ArticleId = articleId, Approved = false, RejectionReason = "Prix invalide." },
                new ModerationDecision { ArticleId = articleId, Approved = true });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var decisions = await client.GetFromJsonAsync<List<ModerationDecision>>($"/decisions/{articleId}");

        Assert.Equal(2, decisions!.Count);
        Assert.All(decisions, d => Assert.Equal(articleId, d.ArticleId));
    }
}
