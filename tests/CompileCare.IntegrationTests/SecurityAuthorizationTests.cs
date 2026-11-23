using System.Net;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using Xunit;

namespace CompileCare.IntegrationTests;

public sealed class SecurityAuthorizationTests(FacilityApiFactory factory) : IClassFixture<FacilityApiFactory>
{
    [Fact]
    public async Task FacilityEndpoints_RejectAnonymousRequests()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TenantScopedEndpoint_RequiresActiveTenant()
    {
        using var client = CreateAuthenticatedClient(
            tenantHeader: null,
            tenantClaims: [Guid.NewGuid()],
            roles: ["HospitalAdmin"]);

        var response = await client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TenantHeader_IsNotAuthorizationAndMustMatchMembershipClaim()
    {
        var permittedTenant = Guid.NewGuid();
        using var client = CreateAuthenticatedClient(
            tenantHeader: Guid.NewGuid(),
            tenantClaims: [permittedTenant],
            roles: ["HospitalAdmin"]);

        var response = await client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FacilityViewer_CanReadButCannotCreate()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        using var client = CreateAuthenticatedClient(
            tenantHeader: tenantId,
            tenantClaims: [tenantId],
            roles: ["FacilityViewer"]);

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
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        using var client = CreateAuthenticatedClient(
            tenantHeader: tenantId,
            tenantClaims: [tenantId],
            roles: ["FacilityManager"]);

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
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        using var client = CreateAuthenticatedClient(
            tenantHeader: tenantId,
            tenantClaims: [tenantId],
            roles: ["HospitalAdmin"]);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Admin Hospital", code = "ADMIN" });
        var created = await createResponse.Content.ReadFromJsonAsync<HospitalResponse>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);

        var deleteResponse = await client.DeleteAsync($"/api/v1/hospitals/{created!.Id:D}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(
        Guid? tenantHeader,
        Guid[] tenantClaims,
        string[] roles)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        client.DefaultRequestHeaders.Add(
            "X-Test-Tenant-Ids",
            string.Join(',', tenantClaims.Select(tenant => tenant.ToString("D"))));
        if (roles.Length > 0)
            client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', roles));
        if (tenantHeader.HasValue)
            client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantHeader.Value.ToString("D"));

        return client;
    }

    private sealed record HospitalResponse(Guid Id);
}
