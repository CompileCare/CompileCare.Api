using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CompileCare.IntegrationTests;

/// <summary>
/// Issues short-lived test-only JWTs so integration tests exercise the real
/// JwtBearer validation pipeline. The key is synthetic and must never be reused.
/// </summary>
public static class TestJwtTokenFactory
{
    public const string Issuer = "https://identity.compilecare.test/";
    public const string Audience = "compilecare-api-tests";
    public const string Subject = "integration-test-user";

    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("compilecare-test-only-key-not-for-production"));

    public static SecurityKey ValidationKey => SigningKey;

    public static string CreateToken(
        string? subject = null,
        string? issuer = null,
        string? audience = null,
        DateTime? expiresUtc = null,
        IEnumerable<Claim>? additionalClaims = null)
    {
        var claims = additionalClaims?.ToList() ?? [];

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? Audience,
            Subject = new ClaimsIdentity(claims),
            Claims = subject is null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object> { ["sub"] = subject },
            NotBefore = now.AddSeconds(-5),
            IssuedAt = now,
            Expires = expiresUtc ?? now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
