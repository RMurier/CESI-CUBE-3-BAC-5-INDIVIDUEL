using Microsoft.EntityFrameworkCore;

namespace Identity.Api.Infrastructure;

/// <summary>
/// EnsureCreated ne fait rien quand la base existe déjà : la table RefreshTokens, ajoutée après le
/// premier déploiement, n'y serait jamais créée (login en erreur 500). Rattrapage idempotent en
/// attendant de vraies migrations EF Core (dette assumée, cf. README section 11).
/// </summary>
public static class IdentitySchemaUpgrades
{
    public static async Task EnsureCreatedAndUpToDateAsync(this IdentityDbContext db)
    {
        var created = await db.Database.EnsureCreatedAsync();
        if (created) return;

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'[RefreshTokens]', N'U') IS NULL
            BEGIN
                CREATE TABLE [RefreshTokens] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_RefreshTokens] PRIMARY KEY,
                    [UserId] uniqueidentifier NOT NULL,
                    [TokenHash] nvarchar(64) NOT NULL,
                    [ExpiresAt] datetimeoffset NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [RevokedAt] datetimeoffset NULL
                );
                CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
                CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]);
            END

            IF OBJECT_ID(N'[ConfirmationTokens]', N'U') IS NULL
            BEGIN
                CREATE TABLE [ConfirmationTokens] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_ConfirmationTokens] PRIMARY KEY,
                    [UserId] uniqueidentifier NOT NULL,
                    [TokenHash] nvarchar(64) NOT NULL,
                    [ExpiresAt] datetimeoffset NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [ConfirmedAt] datetimeoffset NULL
                );
                CREATE UNIQUE INDEX [IX_ConfirmationTokens_TokenHash] ON [ConfirmationTokens] ([TokenHash]);
                CREATE INDEX [IX_ConfirmationTokens_UserId] ON [ConfirmationTokens] ([UserId]);
            END

            IF COL_LENGTH(N'Users', N'IsEmailConfirmed') IS NULL
            BEGIN
                ALTER TABLE [Users] ADD [IsEmailConfirmed] bit NOT NULL CONSTRAINT [DF_Users_IsEmailConfirmed] DEFAULT 0;
            END
            """);
    }
}
