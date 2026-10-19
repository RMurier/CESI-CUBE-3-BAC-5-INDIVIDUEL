using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Catalog.Api.Contracts;
using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Catalog.Api.Payments;
using Collector.Contracts.Events;
using Collector.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Endpoints;

public static class OrderEndpoints
{
    // Une session Stripe Checkout vit 30 min : tant qu'une commande en attente plus récente existe
    // pour l'article, on refuse d'en ouvrir une autre (évite deux acheteurs payant le même objet unique).
    private static readonly TimeSpan CheckoutLifetime = TimeSpan.FromMinutes(30);

    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", CreateOrder).RequireAuthorization();
        app.MapGet("/orders/mine", async (ClaimsPrincipal principal, CatalogDbContext db) =>
        {
            var buyerId = UserId(principal);
            var orders = await db.Orders.Where(o => o.BuyerId == buyerId).OrderByDescending(o => o.CreatedAt).ToListAsync();
            return Results.Ok(orders.Select(OrderResponse.From));
        }).RequireAuthorization();
        app.MapGet("/orders/{id:guid}", async (Guid id, ClaimsPrincipal principal, CatalogDbContext db) =>
        {
            var userId = UserId(principal);
            var order = await db.Orders.FindAsync(id);
            // 404 (et non 403) pour un tiers : ne pas révéler l'existence de la commande.
            return order is not null && (order.BuyerId == userId || order.SellerId == userId)
                ? Results.Ok(OrderResponse.From(order))
                : Results.NotFound();
        }).RequireAuthorization();

        app.MapPost("/webhooks/stripe", HandleStripeWebhook).AllowAnonymous();
    }

    private static async Task<IResult> CreateOrder(
        CreateOrderRequest request,
        ClaimsPrincipal principal,
        CatalogDbContext db,
        IPaymentGateway payments,
        IConfiguration config,
        CancellationToken cancellationToken)
    {
        var buyerId = UserId(principal);

        var article = await db.Articles.FindAsync([request.ArticleId], cancellationToken);
        if (article is null) return Results.NotFound();
        if (article.Status != ArticleStatus.Published)
        {
            return Results.Conflict(new { message = "Cet article n'est plus disponible." });
        }

        var shop = await db.Shops.FindAsync([article.ShopId], cancellationToken);
        if (shop is null) return Results.NotFound();
        if (shop.SellerId == buyerId)
        {
            return Results.BadRequest(new { message = "Vous ne pouvez pas acheter votre propre article." });
        }

        // Sans compte Connect actif, on ne pourrait pas reverser le net au vendeur : on refuse AVANT d'encaisser.
        var payoutAccount = await PayoutEndpoints.GetReadyAccountAsync(db, payments, shop.SellerId, cancellationToken);
        if (payoutAccount is null)
        {
            return Results.Conflict(new { message = "Le vendeur n'a pas encore configuré ses paiements." });
        }

        var since = DateTimeOffset.UtcNow - CheckoutLifetime;
        if (await db.Orders.AnyAsync(o => o.ArticleId == article.Id && o.Status == OrderStatus.Pending && o.CreatedAt > since, cancellationToken))
        {
            return Results.Conflict(new { message = "Un paiement est déjà en cours pour cet article." });
        }

        var order = new Order
        {
            ArticleId = article.Id,
            ArticleTitle = article.Title,
            BuyerId = buyerId,
            SellerId = shop.SellerId,
            ItemPrice = article.Price,
            ShippingCost = article.ShippingCost,
            CommissionAmount = Order.ComputeCommission(article.Price + article.ShippingCost),
            SellerAccountId = payoutAccount.StripeAccountId,
        };

        var frontendOrigin = config["Cors:FrontendOrigin"]!.TrimEnd('/');
        CheckoutSessionResult session;
        try
        {
            session = await payments.CreateCheckoutSessionAsync(new CheckoutRequest(
                order.Id, order.ArticleTitle, order.ItemPrice, order.ShippingCost, order.SellerAccountId, order.CommissionAmount,
                SuccessUrl: $"{frontendOrigin}/commandes/{order.Id}?paiement=ok",
                CancelUrl: $"{frontendOrigin}/commandes/{order.Id}?paiement=annule"), cancellationToken);
        }
        catch (PaymentsNotConfiguredException)
        {
            return Results.Problem("Le paiement n'est pas disponible pour le moment.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        order.CheckoutSessionId = session.SessionId;
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/orders/{order.Id}", new { orderId = order.Id, checkoutUrl = session.Url });
    }

    private static async Task<IResult> HandleStripeWebhook(
        HttpRequest httpRequest,
        IPaymentGateway payments,
        CatalogDbContext db,
        IEventBus eventBus,
        ILogger<Order> logger,
        CancellationToken cancellationToken)
    {
        // Corps brut requis tel quel : la signature est calculée sur les octets exacts reçus.
        using var reader = new StreamReader(httpRequest.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        var webhook = payments.ParseWebhook(payload, httpRequest.Headers["Stripe-Signature"]);
        if (webhook is null)
        {
            // Signature invalide OU évènement qui ne nous concerne pas : indistinguable côté appelant à dessein.
            return Results.BadRequest();
        }

        var order = await db.Orders.FindAsync([webhook.OrderId], cancellationToken);
        if (order is null)
        {
            logger.LogWarning("Webhook de paiement pour une commande inconnue {OrderId}", webhook.OrderId);
            return Results.Ok(); // 200 : inutile que le PSP rejoue un évènement que l'on ne peut jamais traiter.
        }

        switch (webhook.Kind)
        {
            case PaymentWebhookKind.Completed when order.Status == OrderStatus.Pending:
                order.Status = OrderStatus.Paid;
                order.PaidAt = DateTimeOffset.UtcNow;
                var article = await db.Articles.FindAsync([order.ArticleId], cancellationToken);
                if (article is not null) article.Status = ArticleStatus.Sold;
                await db.SaveChangesAsync(cancellationToken);

                await eventBus.PublishAsync(new OrderPaidEvent(
                    order.Id, order.ArticleId, order.ArticleTitle, order.BuyerId, order.SellerId,
                    order.TotalAmount, order.CommissionAmount, order.PaidAt.Value), CancellationToken.None);
                break;

            case PaymentWebhookKind.Expired when order.Status == OrderStatus.Pending:
                order.Status = OrderStatus.Cancelled;
                await db.SaveChangesAsync(cancellationToken);
                break;

            // Évènement rejoué (le PSP retente tant qu'il n'a pas eu de 2xx) : sans effet, c'est l'idempotence.
        }

        return Results.Ok();
    }

    private static Guid UserId(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}

public sealed record OrderResponse(
    Guid Id, Guid ArticleId, string ArticleTitle, decimal ItemPrice, decimal ShippingCost,
    decimal TotalAmount, decimal CommissionAmount, string Status, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt)
{
    public static OrderResponse From(Order o) => new(
        o.Id, o.ArticleId, o.ArticleTitle, o.ItemPrice, o.ShippingCost,
        o.TotalAmount, o.CommissionAmount, o.Status.ToString(), o.CreatedAt, o.PaidAt);
}
