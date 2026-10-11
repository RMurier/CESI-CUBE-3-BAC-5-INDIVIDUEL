using Microsoft.EntityFrameworkCore;
using Notification.Api.Domain;

namespace Notification.Api.Infrastructure;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<InterestSubscription> Subscriptions => Set<InterestSubscription>();
    public DbSet<UserNotification> Notifications => Set<UserNotification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InterestSubscription>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.UserId);
            entity.Property(s => s.Keyword).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<UserNotification>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.HasIndex(n => n.UserId);
        });
    }
}
