using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Collector.Observability.Tests;

/// <summary>
/// OmniaClient reproduit le protocole de signature de l'intégration Omnia déjà en place ailleurs
/// (ex. WealthFlowX) : ces tests valident que la requête HTTP produite respecte exactement ce
/// protocole (headers, hash du corps, signature HMAC), pas seulement que "ça compile".
/// </summary>
[Trait("Category", "Unit")]
public sealed class OmniaClientTests
{
    private const string ProjectId = "11111111-1111-1111-1111-111111111111";
    private const string BaseUrl = "https://omnia-monitoring.test";
    private static readonly byte[] SecretBytes = Encoding.UTF8.GetBytes("test-secret-bytes-not-real");
    private static readonly string Secret = Convert.ToBase64String(SecretBytes);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public int CallCount { get; private set; }
        public Exception? ThrowOnSend { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (ThrowOnSend is not null) throw ThrowOnSend;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static (OmniaClient client, CapturingHandler handler) CreateClient(
        string serviceName = "TestService", bool configured = true)
    {
        var handler = new CapturingHandler();
        var httpClient = new HttpClient(handler);

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("omnia")).Returns(httpClient);

        var settings = configured
            ? new Dictionary<string, string?>
            {
                ["Omnia:BaseUrl"] = BaseUrl,
                ["Omnia:ProjectId"] = ProjectId,
                ["Omnia:Secret"] = Secret,
                ["Omnia:Version"] = "1",
            }
            : new Dictionary<string, string?>();

        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return (new OmniaClient(factory.Object, config, serviceName), handler);
    }

    private static string ExpectedSignature(string path, string body, string timestamp, string nonce)
    {
        var bodyHashHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var dataToSign = $"{ProjectId}\n1\n{timestamp}\n{nonce}\nPOST\n{path}\n{bodyHashHex}";
        using var hmac = new HMACSHA256(SecretBytes);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign)));
    }

    [Fact]
    public async Task SendLogAsync_posts_to_api_log_with_a_valid_signature()
    {
        var (client, handler) = CreateClient(serviceName: "Identity");

        await client.SendLogAsync("UnhandledException", "error", "Boom", payload: new { path = "/x" });

        Assert.Equal(1, handler.CallCount);
        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{BaseUrl}/api/log", request.RequestUri!.ToString());

        var timestamp = request.Headers.GetValues("X-Timestamp").Single();
        var nonce = request.Headers.GetValues("X-Nonce").Single();
        var signature = request.Headers.GetValues("X-Signature").Single();
        Assert.Equal(ProjectId, request.Headers.GetValues("X-App-Id").Single());
        Assert.Equal("1", request.Headers.GetValues("X-Key-Version").Single());
        Assert.Equal(ExpectedSignature("/api/log", handler.LastBody!, timestamp, nonce), signature);

        using var json = JsonDocument.Parse(handler.LastBody!);
        var root = json.RootElement;
        Assert.Equal(ProjectId, root.GetProperty("refApplication").GetString());
        // category préfixée par le nom du service : les 5 services partagent un seul ProjectId Omnia,
        // c'est ce préfixe qui les distingue côté dashboard.
        Assert.Equal("Identity.UnhandledException", root.GetProperty("category").GetString());
        Assert.Equal("error", root.GetProperty("level").GetString());
        Assert.Equal("Boom", root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task TrackLoginAsync_posts_to_api_activity()
    {
        var (client, handler) = CreateClient();

        await client.TrackLoginAsync("user-42");

        Assert.Equal(1, handler.CallCount);
        Assert.Equal($"{BaseUrl}/api/activity", handler.LastRequest!.RequestUri!.ToString());
        using var json = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("user-42", json.RootElement.GetProperty("anonymousUserId").GetString());
    }

    [Fact]
    public async Task SendLogAsync_does_nothing_when_not_configured()
    {
        var (client, handler) = CreateClient(configured: false);

        await client.SendLogAsync("Category", "error", "message");

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendLogAsync_swallows_http_failures()
    {
        var (client, handler) = CreateClient();
        handler.ThrowOnSend = new HttpRequestException("Omnia unreachable");

        // Ne doit jamais lever : l'observabilité ne doit pas devenir elle-même une source de panne.
        await client.SendLogAsync("Category", "error", "message");

        Assert.Equal(1, handler.CallCount);
    }
}
