using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace HeapCare.IntegrationTests;

/// <summary>
/// Issues short-lived test-only HS256 JWTs for the real JwtBearer integration pipeline.
/// The signing key and issuer are synthetic; never use them outside tests.
/// </summary>
public static class TestJwtTokenFactory
{
    public const string Issuer = "https://identity.compilecare.test/";
    public const string Audience = "compilecare-api-tests";
    public const string Subject = "integration-test-user";

    private static readonly byte[] SigningKeyBytes =
        Encoding.UTF8.GetBytes("compilecare-test-only-key-not-for-production");

    private static readonly SymmetricSecurityKey SigningKey = new(SigningKeyBytes);

    public static SecurityKey ValidationKey => SigningKey;

    public static string CreateToken(
        string? subject = Subject,
        string? issuer = null,
        string? audience = null,
        DateTime? expiresUtc = null,
        IEnumerable<Claim>? additionalClaims = null)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = expiresUtc is null
            ? now.AddMinutes(5)
            : new DateTimeOffset(DateTime.SpecifyKind(expiresUtc.Value, DateTimeKind.Utc));

        var payload = new Dictionary<string, object?>
        {
            ["iss"] = issuer ?? Issuer,
            ["aud"] = audience ?? Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.AddSeconds(-5).ToUnixTimeSeconds(),
            ["exp"] = expires.ToUnixTimeSeconds()
        };

        if (subject is not null)
            payload["sub"] = subject;

        foreach (var claim in additionalClaims ?? [])
        {
            if (payload.TryGetValue(claim.Type, out var existing))
            {
                if (existing is List<string> values)
                {
                    values.Add(claim.Value);
                }
                else
                {
                    payload[claim.Type] = new List<string>
                    {
                        Convert.ToString(existing, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                        claim.Value
                    };
                }
            }
            else
            {
                payload[claim.Type] = claim.Value;
            }
        }

        var header = new Dictionary<string, object>
        {
            ["alg"] = SecurityAlgorithms.HmacSha256,
            ["typ"] = "JWT"
        };

        var encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var unsignedToken = $"{encodedHeader}.{encodedPayload}";
        var signature = HMACSHA256.HashData(SigningKeyBytes, Encoding.ASCII.GetBytes(unsignedToken));

        return $"{unsignedToken}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
