using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Catalog.Api.Payments;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Endpoints;

/// <summary>Onboarding Stripe Connect des vendeurs : prérequis pour recevoir le net d'une vente.</summary>
public static class PayoutEndpoints
{
    public static void MapPayoutEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/seller/payout-account").RequireAuthorization(policy => policy.RequireRole("Seller"));

        // Crée le compte Connect si besoin et renvoie le lien d'onboarding hébergé par Stripe.
        group.MapPost("/onboarding", async (
            ClaimsPrincipal principal,
            CatalogDbContext db,
            IPaymentGateway payments,
            IConfiguration config,
            CancellationToken cancellationToken) =>
        {
            var sellerId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
            try
            {
                var account = await db.SellerPayoutAccounts.FindAsync([sellerId], cancellationToken);
                if (account is null)
                {
                    account = new SellerPayoutAccount
                    {
                        SellerId = sellerId,
                        StripeAccountId = await payments.CreateSellerAccountAsync(sellerId, cancellationToken),
                    };
                    db.SellerPayoutAccounts.Add(account);
                    await db.SaveChangesAsync(cancellationToken);
                }

                var frontendOrigin = config["Cors:FrontendOrigin"]!.TrimEnd('/');
                var url = await payments.CreateOnboardingLinkAsync(
                    account.StripeAccountId,
                    returnUrl: $"{frontendOrigin}/compte?paiements=retour",
                    refreshUrl: $"{frontendOrigin}/compte?paiements=reprise",
                    cancellationToken);
                return Results.Ok(new { url });
            }
            catch (PaymentsNotConfiguredException)
            {
                return Results.Problem("Le paiement n'est pas disponible pour le moment.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        // État du compte, relu chez Stripe tant qu'il n'est pas prêt (l'onboarding se termine hors de notre application).
        group.MapGet("/", async (
            ClaimsPrincipal principal,
            CatalogDbContext db,
            IPaymentGateway payments,
            CancellationToken cancellationToken) =>
        {
            var sellerId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
            try
            {
                var ready = await GetReadyAccountAsync(db, payments, sellerId, cancellationToken) is not null;
                var configured = ready || await db.SellerPayoutAccounts.AnyAsync(a => a.SellerId == sellerId, cancellationToken);
                return Results.Ok(new { configured, ready });
            }
            catch (PaymentsNotConfiguredException)
            {
                return Results.Ok(new { configured = false, ready = false });
            }
        });
    }

    /// <summary>Renvoie le compte du vendeur s'il peut recevoir des transferts (en relisant Stripe si besoin), sinon null.</summary>
    public static async Task<SellerPayoutAccount?> GetReadyAccountAsync(
        CatalogDbContext db, IPaymentGateway payments, Guid sellerId, CancellationToken cancellationToken)
    {
        var account = await db.SellerPayoutAccounts.FindAsync([sellerId], cancellationToken);
        if (account is null) return null;
        if (account.Ready) return account;

        account.Ready = await payments.IsSellerAccountReadyAsync(account.StripeAccountId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return account.Ready ? account : null;
    }
}
