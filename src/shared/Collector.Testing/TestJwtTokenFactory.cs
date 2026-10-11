using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Collector.Testing;

/// <summary>
/// Émet des JWT valides côté test, signés avec la même clé que celle injectée par
/// <see cref="SqlServerWebApplicationFactory{TEntryPoint, TDbContext}"/> dans la config de test.
/// Évite de dépendre du service Identity réel pour tester les endpoints protégés des autres services.
/// </summary>
public static class TestJwtTokenFactory
{
    public const string SigningKey = "integration-test-signing-key-minimum-32-characters!";
    public const string Issuer = "collector-identity";
    public const string Audience = "collector-api";

    public static string IssueToken(Guid userId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, $"{userId}@test.collector.shop"),
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
