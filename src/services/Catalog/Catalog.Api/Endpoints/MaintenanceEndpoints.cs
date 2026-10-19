using System.Security.Cryptography;
using System.Text;
using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Endpoints;

/// <summary>
/// Traitements récurrents pilotés par Airflow (voir infra/airflow/dags). Protégés par une clé partagée
/// (en-tête X-Maintenance-Key, secret Infisical Maintenance__ApiKey) ; absents (404) si la clé n'est pas configurée.
/// </summary>
public static class MaintenanceEndpoints
{
    // Une session Stripe Checkout expire après 30 min : au-delà, une commande encore "Pending" est abandonnée.
    private static readonly TimeSpan StalePendingAfter = TimeSpan.FromMinutes(35);

    public static void MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal").AddEndpointFilter(RequireMaintenanceKey);

        // Filet de sécurité si le webhook checkout.session.expired n'est jamais arrivé : libère l'article.
        group.MapPost("/maintenance/expire-pending-orders", async (CatalogDbContext db, CancellationToken cancellationToken) =>
        {
            var cutoff = DateTimeOffset.UtcNow - StalePendingAfter;
            var stale = await db.Orders.Where(o => o.Status == OrderStatus.Pending && o.CreatedAt < cutoff).ToListAsync(cancellationToken);
            foreach (var order in stale) order.Status = OrderStatus.Cancelled;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { cancelled = stale.Count });
        });

        // Rapprochement : commissions encaissées sur une période (contrôle comptable quotidien).
        group.MapGet("/reports/commissions", async (DateTimeOffset from, DateTimeOffset to, CatalogDbContext db, CancellationToken cancellationToken) =>
        {
            var paid = await db.Orders
                .Where(o => o.Status == OrderStatus.Paid && o.PaidAt >= from && o.PaidAt < to)
                .Select(o => new { o.ItemPrice, o.ShippingCost, o.CommissionAmount })
                .ToListAsync(cancellationToken);
            var total = paid.Sum(o => o.ItemPrice + o.ShippingCost);
            var commission = paid.Sum(o => o.CommissionAmount);
            return Results.Ok(new { from, to, orders = paid.Count, totalAmount = total, commissionAmount = commission, sellerAmount = total - commission });
        });
    }

    private static async ValueTask<object?> RequireMaintenanceKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Maintenance:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected)) return Results.NotFound();

        var provided = context.HttpContext.Request.Headers["X-Maintenance-Key"].ToString();
        // Comparaison à temps constant : ne pas laisser fuiter la clé par le temps de réponse.
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
        return ok ? await next(context) : Results.Unauthorized();
    }
}
