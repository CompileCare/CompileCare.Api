using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CompileCare.IntegrationTests;

/// <summary>
/// Test-only authentication. It is registered exclusively by FacilityApiFactory
/// when the host runs in the Testing environment.
/// </summary>
public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "CompileCareTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Authenticated", out var authenticated) ||
            !string.Equals(authenticated.ToString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "integration-test-user"),
            new("sub", "integration-test-user")
        };

        AddClaims("X-Test-Tenant-Ids", AuthorizationPolicies.TenantIdClaim, claims);
        AddClaims("X-Test-Roles", "role", claims);
        AddClaims("X-Test-Permissions", AuthorizationPolicies.PermissionClaim, claims);

        var identity = new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, "role");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private void AddClaims(string headerName, string claimType, ICollection<Claim> claims)
    {
        var values = Request.Headers[headerName].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var value in values)
            claims.Add(new Claim(claimType, value));
    }
}
