using Microsoft.EntityFrameworkCore;
using Moderation.Api.Domain;

namespace Moderation.Api.Infrastructure;

public sealed class ModerationDbContext(DbContextOptions<ModerationDbContext> options) : DbContext(options)
{
    public DbSet<ModerationDecision> Decisions => Set<ModerationDecision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModerationDecision>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.HasIndex(d => d.ArticleId);
        });
    }
}
