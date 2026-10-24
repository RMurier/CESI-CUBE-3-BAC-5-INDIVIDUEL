using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Notification.Api.Infrastructure;
using Xunit;

namespace Notification.Api.Tests;

[Trait("Category", "Unit")]
public sealed class SmtpEmailSenderTests
{
    private sealed class StubHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    [Fact]
    public async Task SendAsync_does_not_throw_when_the_user_email_cannot_be_resolved()
    {
        HttpClient httpClient = new(new StubHandler(HttpStatusCode.NotFound)) { BaseAddress = new Uri("http://identity.test") };
        StaticHttpClientFactory factory = new(httpClient);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Maintenance:ApiKey"] = "test-key" })
            .Build();
        SmtpEmailSender sender = new(factory, configuration, NullLogger<SmtpEmailSender>.Instance);

        // L'email introuvable (404) fait retourner avant toute tentative SmtpClient — aucune exception attendue.
        await sender.SendAsync(Guid.NewGuid(), "Sujet", "Corps");
    }
}
