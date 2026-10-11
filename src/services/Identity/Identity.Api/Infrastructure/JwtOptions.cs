namespace Identity.Api.Infrastructure;

/// <summary>
/// Lu depuis la section "Jwt" de la config. En environnement K8s, SigningKey arrive via un Secret
/// synchronisé par Infisical (jamais en dur dans appsettings.json ni dans le repo).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string SigningKey { get; set; }
    public string Issuer { get; set; } = "collector-identity";
    public string Audience { get; set; } = "collector-api";
    public int ExpiryMinutes { get; set; } = 60;
}
