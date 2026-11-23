using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace CompileCare.IntegrationTests;

/// <summary>Test-only principal issuer. Production authentication always uses OIDC JWT bearer.</summary>
public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string TestScheme = "CompileCareTest";
    public const string Issuer = "https://identity.compilecare.test/";
    public const string Subject = "integration-test-user";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Authenticated", out var authenticated) ||
            !string.Equals(authenticated.ToString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var issuer = Request.Headers["X-Test-Issuer"].FirstOrDefault() ?? Issuer;
        var subject = Request.Headers["X-Test-Subject"].FirstOrDefault() ?? Subject;
        var claims = new List<Claim>
        {
            new("iss", issuer),
            new("sub", subject)
        };

        claims.AddRange(ReadClaims("X-Test-External-Permissions", "compilecare:permission"));
        claims.AddRange(ReadClaims("X-Test-External-Roles", "role"));

        var identity = new ClaimsIdentity(claims, TestScheme, "sub", "role");

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
    }

    private IEnumerable<Claim> ReadClaims(string headerName, string claimType) =>
        Request.Headers[headerName]
            .ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => new Claim(claimType, value));
}
