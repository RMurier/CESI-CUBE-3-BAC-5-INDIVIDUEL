using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Collector.Observability;

/// <summary>
/// Même protocole de signature que l'intégration Omnia existante sur les autres projets de
/// l'utilisateur (HMAC-SHA256 sur "projectId\nversion\ntimestamp\nnonce\nPOST\npath\nbodyHash") —
/// reproduit à l'identique ici, pas réinventé, pour rester compatible avec le serveur Omnia tel quel.
/// </summary>
public sealed class OmniaClient : IOmniaClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _serviceName;
    private readonly string _baseUrl;
    private readonly string _projectId;
    private readonly byte[] _secretBytes;
    private readonly string _version;
    private readonly bool _isConfigured;

    public OmniaClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, string serviceName)
    {
        _httpClientFactory = httpClientFactory;
        _serviceName = serviceName;

        string? baseUrl = configuration["Omnia:BaseUrl"];
        string? projectId = configuration["Omnia:ProjectId"];
        string? secret = configuration["Omnia:Secret"];

        _isConfigured = !string.IsNullOrEmpty(baseUrl)
                     && !string.IsNullOrEmpty(projectId)
                     && !string.IsNullOrEmpty(secret);

        _baseUrl = baseUrl?.TrimEnd('/') ?? string.Empty;
        _projectId = projectId ?? string.Empty;
        _secretBytes = _isConfigured ? Convert.FromBase64String(secret!) : [];
        _version = configuration["Omnia:Version"] ?? "1";
    }

    public async Task SendLogAsync(string category, string level, string message, object? payload = null, CancellationToken ct = default)
    {
        if (!_isConfigured) return;

        object body = new
        {
            refApplication = _projectId,
            category = $"{_serviceName}.{category}",
            level,
            message,
            payloadJson = JsonSerializer.Serialize(payload ?? new { }),
            occurredAtUtc = DateTime.UtcNow,
        };

        await PostWithHmacAsync("/api/log", body, ct);
    }

    public async Task TrackLoginAsync(string userId, CancellationToken ct = default)
    {
        if (!_isConfigured) return;

        object body = new
        {
            refApplication = _projectId,
            anonymousUserId = userId,
            connectedAtUtc = DateTime.UtcNow,
        };

        await PostWithHmacAsync("/api/activity", body, ct);
    }

    private async Task PostWithHmacAsync(string path, object body, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(body);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(json);
        string bodyHashHex = Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant();

        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        string nonce = Guid.NewGuid().ToString("N");

        string dataToSign = $"{_projectId}\n{_version}\n{timestamp}\n{nonce}\nPOST\n{path}\n{bodyHashHex}";
        string signature = ComputeHmacSha256(dataToSign);

        HttpClient http = _httpClientFactory.CreateClient("omnia");
        HttpRequestMessage request = new(HttpMethod.Post, $"{_baseUrl}{path}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-App-Id", _projectId);
        request.Headers.Add("X-Key-Version", _version);
        request.Headers.Add("X-Timestamp", timestamp);
        request.Headers.Add("X-Nonce", nonce);
        request.Headers.Add("X-Signature", signature);

        // Best-effort : un Omnia indisponible ne doit jamais faire échouer la requête applicative
        // qui a déclenché ce log (c'est un appel secondaire, pas le flux principal).
        try
        {
            await http.SendAsync(request, ct);
        }
        catch (Exception)
        {
            // Rien à faire : l'observabilité elle-même ne doit pas devenir une source de panne.
        }
    }

    private string ComputeHmacSha256(string data)
    {
        using HMACSHA256 hmac = new(_secretBytes);
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }
}
