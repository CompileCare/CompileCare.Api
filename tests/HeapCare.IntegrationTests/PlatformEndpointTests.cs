using System.Net;
using System.Net.Http.Headers;
using HeapCare.Api.Middleware;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class PlatformEndpointTests
{
    private readonly FacilityApiFactory _factory;

    public PlatformEndpointTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task LiveEndpoint_IsHealthy()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidTenantHeader_IsRejectedWithProblemDetails()
    {
        using var client = CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, "bad");

        var response = await client.GetAsync("/api/v1/platform/context");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task PlatformOperatorStatus_ReturnsFalseForAnAuthenticatedTenantUser()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/v1/platform/operator/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("isPlatformOperator").GetBoolean());
    }

    [Fact]
    public async Task PlatformOperatorStatus_UsesThePersistedValidatedIdentity()
    {
        await _factory.SeedPlatformOperatorAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/v1/platform/operator/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("isPlatformOperator").GetBoolean());
    }

    [Fact]
    public async Task PlatformOperatorStatus_ReturnsFalseForAnInactivePersistedOperator()
    {
        await _factory.SeedPlatformOperatorAsync(isActive: false);
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/v1/platform/operator/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("isPlatformOperator").GetBoolean());
    }

    [Fact]
    public async Task PlatformTenantEndpoints_RejectTenantUserEvenWhenTokenClaimsPlatformOperator()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            HeapCare.Application.Tenancy.TenantRoles.HospitalAdmin);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateToken(additionalClaims:
            [
                new System.Security.Claims.Claim("role", "PlatformOperator"),
                new System.Security.Claims.Claim("platform_operator", "true"),
                new System.Security.Claims.Claim("permission", "platform.tenants.manage")
            ]));

        var response = await client.GetAsync("/api/v1/platform/tenants/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlatformOperatorStatus_RequiresAuthentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/platform/operator/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CorrelationIdMiddleware_PreservesValidOpaqueCorrelationIds()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "request-correlation-123");

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "request-correlation-123",
            response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task CorrelationIdMiddleware_ReplacesUnsafeCorrelationIds()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "invalid/correlation");

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.NotEqual("invalid/correlation", correlationId);
        Assert.True(correlationId.Length <= 64);
        Assert.All(correlationId, character =>
            Assert.True(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken());
        return client;
    }
}
