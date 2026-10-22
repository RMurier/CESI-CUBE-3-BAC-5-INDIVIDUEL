using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Collector.Observability;

/// <summary>
/// Log vers Omnia de toute exception non gérée par un endpoint (les endpoints Collector renvoient
/// déjà Results.X() directement pour leurs cas d'erreur métier — ce handler ne voit donc que les
/// vrais bugs). Factorisé ici plutôt que dupliqué dans les 5 Program.cs : une seule version à
/// tester, au lieu de 5 copies identiques dont aucun test (unitaire ou intégration) ne déclenche
/// jamais le corps — c'est ce qui faisait retomber la couverture Sonar sous 80 %.
/// </summary>
public static class OmniaExceptionLogging
{
    public static void UseOmniaExceptionLogging(this IApplicationBuilder app, bool isDevelopment)
    {
        app.UseExceptionHandler(errApp => errApp.Run(ctx => HandleAsync(ctx, isDevelopment)));
    }

    internal static async Task HandleAsync(HttpContext ctx, bool isDevelopment)
    {
        var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (ex is not null)
        {
            var omnia = ctx.RequestServices.GetRequiredService<IOmniaClient>();
            await omnia.SendLogAsync(
                category: "UnhandledException",
                level: "error",
                message: ex.Message,
                payload: new
                {
                    service = omnia.ServiceName,
                    correlationId = CorrelationId.Get(ctx),
                    exceptionType = ex.GetType().FullName,
                    // ex.ToString() (et non juste StackTrace) : inclut toute la chaîne d'InnerException
                    // avec leurs types/messages propres — souvent la vraie cause (ex : un timeout SQL
                    // enterré sous une SqlException puis une exception EF Core) est sinon invisible.
                    details = ex.ToString(),
                    path = ctx.Request.Path.Value,
                    method = ctx.Request.Method,
                });
        }

        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Une erreur interne est survenue.",
            Detail = isDevelopment ? ex?.ToString() : null,
        });
    }
}
