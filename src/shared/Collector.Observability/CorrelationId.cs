using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Collector.Observability;

/// <summary>
/// Un identifiant unique par requête HTTP, propagé aux événements RabbitMQ qui en découlent (comme
/// propriété AMQP CorrelationId, voir RabbitMqEventBus/RabbitMqConsumer dans Collector.Messaging) et
/// inclus dans tous les logs Omnia — pour pouvoir suivre une même transaction à travers les 5
/// services malgré le découplage asynchrone (RabbitMQ), au lieu de recouper des logs à la main.
/// </summary>
public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";
    private const string ItemsKey = "CorrelationId";

    /// <summary>Reprend le X-Correlation-Id du client si présent (ex. relayé par le frontend),
    /// sinon en génère un nouveau. Renvoyé dans la réponse pour que l'appelant puisse le logger.</summary>
    public static void UseCorrelationId(this IApplicationBuilder app)
    {
        app.Use(async (ctx, next) =>
        {
            var correlationId = ctx.Request.Headers.TryGetValue(HeaderName, out var existing) && existing.Count > 0 && !string.IsNullOrWhiteSpace(existing[0])
                ? existing[0]!
                : Guid.NewGuid().ToString("N");

            ctx.Items[ItemsKey] = correlationId;
            ctx.Response.Headers[HeaderName] = correlationId;

            await next();
        });
    }

    /// <summary>Null hors contexte HTTP (ex. appelé depuis un BackgroundService) ou si UseCorrelationId
    /// n'a pas tourné sur cette requête (tests qui construisent un HttpContext à la main).</summary>
    public static string? Get(HttpContext ctx) => ctx.Items.TryGetValue(ItemsKey, out var value) ? value as string : null;

    /// <summary>Même chose via IHttpContextAccessor, pour un service singleton sans HttpContext direct
    /// (ex. RabbitMqEventBus) — null si appelé hors d'une requête HTTP (ex. un BackgroundService).</summary>
    public static string? Get(IHttpContextAccessor accessor) => accessor.HttpContext is { } ctx ? Get(ctx) : null;
}
