using System.Net;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Tenancy;
using Xunit;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class SecurityAuthorizationTests
{
    private readonly FacilityApiFactory _factory;

    public SecurityAuthorizationTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task FacilityEndpoints_RejectAnonymousRequests()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Authentication required", problem.RootElement.GetProperty("title").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task TenantScopedEndpoint_RequiresTenantSelection()
    {
        using var client = CreateAuthenticatedClient(tenantId: null);

        var response = await client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TenantHeader_IsNotAuthorizationAndRequiresActiveMembership()
    {
        var permittedTenant = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(permittedTenant, TenantRoles.HospitalAdmin);
        using var client = CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InactiveMembership_IsDenied()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin, isActive: false);
        using var client = CreateAuthenticatedClient(tenantId);

        var response = await client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FacilityViewer_CanReadButCannotCreate()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.FacilityViewer);
        using var client = CreateAuthenticatedClient(tenantId);

        var listResponse = await client.GetAsync("/api/v1/hospitals");
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Viewer should not create", code = "VIEWER" });

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    [Fact]
    public async Task FacilityManager_CanCreateButCannotDelete()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.FacilityManager);
        using var client = CreateAuthenticatedClient(tenantId);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Managed Hospital", code = "MANAGED" });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HospitalResponse>();
        Assert.NotNull(created);

        var deleteResponse = await client.DeleteAsync($"/api/v1/hospitals/{created!.Id:D}");

        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task HospitalAdmin_CanDeleteFacility()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin);
        using var client = CreateAuthenticatedClient(tenantId);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Admin Hospital", code = "ADMIN" });
        var created = await createResponse.Content.ReadFromJsonAsync<HospitalResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);

        var deleteResponse = await client.DeleteAsync($"/api/v1/hospitals/{created!.Id:D}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task ExternalTokenPermissionAndRoleClaimsCannotEscalateTenantMembership()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.FacilityViewer);
        using var client = CreateAuthenticatedClient(tenantId);

        // Simulate claims emitted by the configured identity provider. Only the
        // CompileCare membership row is authoritative for tenant role/permissions.
        client.DefaultRequestHeaders.Add("X-Test-External-Permissions", TenantPermissions.FacilityWrite);
        client.DefaultRequestHeaders.Add("X-Test-External-Roles", TenantRoles.HospitalAdmin);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Role escalation attempt", code = "ESCALATE" });

        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(Guid? tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        client.DefaultRequestHeaders.Add("X-Test-Issuer", TestAuthenticationHandler.Issuer);
        client.DefaultRequestHeaders.Add("X-Test-Subject", TestAuthenticationHandler.Subject);
        if (tenantId.HasValue)
        {
            client.DefaultRequestHeaders.Add(
                TenantContextMiddleware.HeaderName,
                tenantId.Value.ToString("D"));
        }

        return client;
    }

    private sealed record HospitalResponse(Guid Id);
}
