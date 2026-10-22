using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Collector.Observability.Tests;

/// <summary>
/// Les 5 services appellent tous UseOmniaExceptionLogging (voir le fichier éponyme) — ces tests
/// couvrent une seule fois le corps du handler, plutôt que de dépendre d'un test d'intégration qui
/// déclencherait une vraie exception dans chacun des 5 services pour l'exercer.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OmniaExceptionLoggingTests
{
    private static DefaultHttpContext CreateContext(Exception? error, IOmniaClient omnia)
    {
        var services = new ServiceCollection();
        services.AddSingleton(omnia);
        var ctx = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
        ctx.Request.Path = "/shops";
        ctx.Request.Method = "POST";
        if (error is not null)
        {
            ctx.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature { Error = error });
        }
        return ctx;
    }

    [Fact]
    public async Task HandleAsync_sends_the_exception_to_Omnia_and_returns_a_generic_500()
    {
        var omnia = new Mock<IOmniaClient>();
        var error = new InvalidOperationException("Boom");
        var ctx = CreateContext(error, omnia.Object);

        await OmniaExceptionLogging.HandleAsync(ctx, isDevelopment: false);

        omnia.Verify(o => o.SendLogAsync(
            "UnhandledException", "error", "Boom", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(StatusCodes.Status500InternalServerError, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(ctx.Response.Body);
        Assert.Equal("Une erreur interne est survenue.", json.RootElement.GetProperty("title").GetString());
        // Pas de détail d'exception en dehors du développement : ne jamais exposer une stack trace en prod.
        Assert.False(json.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is not null);
    }

    [Fact]
    public async Task HandleAsync_includes_the_exception_detail_only_in_development()
    {
        var omnia = new Mock<IOmniaClient>();
        var error = new InvalidOperationException("Boom");
        var ctx = CreateContext(error, omnia.Object);

        await OmniaExceptionLogging.HandleAsync(ctx, isDevelopment: true);

        ctx.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(ctx.Response.Body);
        Assert.Contains("Boom", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task HandleAsync_does_not_call_Omnia_when_there_is_no_captured_exception()
    {
        var omnia = new Mock<IOmniaClient>();
        var ctx = CreateContext(error: null, omnia.Object);

        await OmniaExceptionLogging.HandleAsync(ctx, isDevelopment: false);

        omnia.Verify(o => o.SendLogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(StatusCodes.Status500InternalServerError, ctx.Response.StatusCode);
    }
}
