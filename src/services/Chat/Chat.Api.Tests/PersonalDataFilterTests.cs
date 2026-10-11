using Chat.Api.Domain;
using Xunit;

namespace Chat.Api.Tests;

public sealed class PersonalDataFilterTests
{
    [Theory]
    [InlineData("Contacte-moi à alice.vendeuse@gmail.com stp", "[email masqué]")]
    [InlineData("mon mail c'est bob@collector.shop", "[email masqué]")]
    public void Sanitize_masks_email_addresses(string input, string expectedFragment)
    {
        var result = PersonalDataFilter.Sanitize(input);

        Assert.Contains(expectedFragment, result);
        Assert.DoesNotContain("@", result);
    }

    [Theory]
    [InlineData("appelle-moi au 06 12 34 56 78")]
    [InlineData("tel: 0612345678")]
    [InlineData("+33 6 12 34 56 78 dispo ce soir")]
    public void Sanitize_masks_french_phone_numbers(string input)
    {
        var result = PersonalDataFilter.Sanitize(input);

        Assert.Contains("[téléphone masqué]", result);
    }

    [Fact]
    public void Sanitize_leaves_ordinary_messages_untouched()
    {
        const string message = "Bonjour, l'article est-il toujours disponible ?";

        var result = PersonalDataFilter.Sanitize(message);

        Assert.Equal(message, result);
    }
}
