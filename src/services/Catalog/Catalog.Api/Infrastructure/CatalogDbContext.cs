using Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<SellerPayoutAccount> SellerPayoutAccounts => Set<SellerPayoutAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shop>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).HasMaxLength(128).IsRequired();
            entity.HasMany(s => s.Articles).WithOne().HasForeignKey(a => a.ShopId);
        });

        modelBuilder.Entity<Article>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Title).HasMaxLength(200).IsRequired();
            entity.Property(a => a.Price).HasColumnType("decimal(10,2)");
            entity.Property(a => a.ShippingCost).HasColumnType("decimal(10,2)");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.HasIndex(o => o.BuyerId);
            entity.HasIndex(o => o.ArticleId);
            entity.HasIndex(o => o.CheckoutSessionId);
            entity.Property(o => o.ArticleTitle).HasMaxLength(200).IsRequired();
            entity.Property(o => o.ItemPrice).HasColumnType("decimal(10,2)");
            entity.Property(o => o.ShippingCost).HasColumnType("decimal(10,2)");
            entity.Property(o => o.CheckoutSessionId).HasMaxLength(255);
            entity.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(o => o.CommissionAmount).HasColumnType("decimal(10,2)");
            entity.Property(o => o.SellerAccountId).HasMaxLength(64).IsRequired();
            // Montants dérivés des valeurs figées : non persistés.
            entity.Ignore(o => o.TotalAmount);
            entity.Ignore(o => o.SellerAmount);
        });

        modelBuilder.Entity<SellerPayoutAccount>(entity =>
        {
            entity.HasKey(a => a.SellerId);
            entity.Property(a => a.StripeAccountId).HasMaxLength(64).IsRequired();
        });
    }
}
