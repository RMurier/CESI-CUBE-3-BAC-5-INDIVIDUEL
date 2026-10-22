using Xunit;

namespace Collector.Observability.Tests;

[Trait("Category", "Unit")]
public sealed class ExceptionDetailsTests
{
    [Fact]
    public void RootMessage_returns_the_outer_message_when_there_is_no_inner_exception()
    {
        var ex = new InvalidOperationException("Boom");

        Assert.Equal("Boom", ExceptionDetails.RootMessage(ex));
    }

    [Fact]
    public void RootMessage_returns_the_deepest_inner_exception_message()
    {
        // EF Core enrobe systématiquement ses erreurs avec un message générique inutile : c'est la
        // cause la plus profonde (ici, un timeout réseau) qui dit vraiment ce qui s'est passé.
        var root = new TimeoutException("La connexion au serveur a expiré.");
        var middle = new InvalidOperationException("An error occurred while saving the entity changes.", root);
        var outer = new InvalidOperationException("Wrapped again", middle);

        Assert.Equal("La connexion au serveur a expiré.", ExceptionDetails.RootMessage(outer));
    }
}
