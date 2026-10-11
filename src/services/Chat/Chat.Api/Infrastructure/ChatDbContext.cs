using Chat.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Chat.Api.Infrastructure;

public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => m.ConversationId);
            entity.Property(m => m.Content).HasMaxLength(2000).IsRequired();
        });
    }
}
