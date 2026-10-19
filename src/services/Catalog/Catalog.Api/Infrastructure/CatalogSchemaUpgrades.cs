using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Infrastructure;

/// <summary>
/// EnsureCreated ne fait rien quand la base existe déjà : une table ajoutée après le premier
/// déploiement (ici Orders et SellerPayoutAccounts) n'y serait jamais créée. Ce rattrapage idempotent comble le trou en
/// attendant de vraies migrations EF Core (dette assumée, cf. README section 11).
/// </summary>
public static class CatalogSchemaUpgrades
{
    public static async Task EnsureCreatedAndUpToDateAsync(this CatalogDbContext db)
    {
        var created = await db.Database.EnsureCreatedAsync();
        if (created) return;

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'[Orders]', N'U') IS NULL
            BEGIN
                CREATE TABLE [Orders] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_Orders] PRIMARY KEY,
                    [ArticleId] uniqueidentifier NOT NULL,
                    [ArticleTitle] nvarchar(200) NOT NULL,
                    [BuyerId] uniqueidentifier NOT NULL,
                    [SellerId] uniqueidentifier NOT NULL,
                    [ItemPrice] decimal(10,2) NOT NULL,
                    [ShippingCost] decimal(10,2) NOT NULL,
                    [CommissionAmount] decimal(10,2) NOT NULL,
                    [SellerAccountId] nvarchar(64) NOT NULL,
                    [Status] nvarchar(16) NOT NULL,
                    [CheckoutSessionId] nvarchar(255) NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [PaidAt] datetimeoffset NULL
                );
                CREATE INDEX [IX_Orders_BuyerId] ON [Orders] ([BuyerId]);
                CREATE INDEX [IX_Orders_ArticleId] ON [Orders] ([ArticleId]);
                CREATE INDEX [IX_Orders_CheckoutSessionId] ON [Orders] ([CheckoutSessionId]);
            END

            IF OBJECT_ID(N'[SellerPayoutAccounts]', N'U') IS NULL
            BEGIN
                CREATE TABLE [SellerPayoutAccounts] (
                    [SellerId] uniqueidentifier NOT NULL CONSTRAINT [PK_SellerPayoutAccounts] PRIMARY KEY,
                    [StripeAccountId] nvarchar(64) NOT NULL,
                    [Ready] bit NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL
                );
            END
            """);
    }
}
