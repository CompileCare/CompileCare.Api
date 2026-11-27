using System.Net;
using System.Net.Http.Json;
using HeapCare.Api.Middleware;
using HeapCare.Api.Security;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Xunit;

namespace HeapCare.IntegrationTests;

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
    public async Task JwtBearer_IsDefaultAuthenticationScheme()
    {
        var schemes = _factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var authenticate = await schemes.GetDefaultAuthenticateSchemeAsync();
        var challenge = await schemes.GetDefaultChallengeSchemeAsync();

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, authenticate?.Name);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, challenge?.Name);
    }

    [Fact]
    public async Task JwtBearer_RejectsUntrustedIssuerAndAudience()
    {
        var wrongIssuer = _factory.CreateClient();
        wrongIssuer.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                TestJwtTokenFactory.CreateToken(issuer: "https://untrusted.example.test/"));
        var issuerResponse = await wrongIssuer.GetAsync("/api/v1/hospitals");

        var wrongAudience = _factory.CreateClient();
        wrongAudience.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                TestJwtTokenFactory.CreateToken(audience: "another-api"));
        var audienceResponse = await wrongAudience.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.Unauthorized, issuerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, audienceResponse.StatusCode);
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
    public async Task ValidJwtWithoutTenantMembership_ReachesMembershipAuthorization()
    {
        using var client = CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync("/api/v1/hospitals");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task JwtBearer_RejectsMissingSubjectAndExpiredTokens()
    {
        using var missingSubject = _factory.CreateClient();
        missingSubject.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                TestJwtTokenFactory.CreateToken(subject: null));
        var missingSubjectResponse = await missingSubject.GetAsync("/api/v1/hospitals");

        using var expiredToken = _factory.CreateClient();
        expiredToken.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                TestJwtTokenFactory.CreateToken(expiresUtc: DateTime.UtcNow.AddMinutes(-5)));
        var expiredTokenResponse = await expiredToken.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.Unauthorized, missingSubjectResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, expiredTokenResponse.StatusCode);
    }

    [Fact]
    public async Task TenantScopedEndpoint_RequiresTenantSelection()
    {
        using var client = CreateAuthenticatedClient(tenantId: null);

        var response = await client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TenantScopedEndpoint_RejectsMalformedEmptyAndMultipleTenantHeaders()
    {
        static HttpClient CreateClient(FacilityApiFactory factory, params string[] tenantHeaderValues)
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", TestJwtTokenFactory.CreateToken());
            client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantHeaderValues);
            return client;
        }

        using var malformed = CreateClient(_factory, "not-a-guid");
        using var empty = CreateClient(_factory, Guid.Empty.ToString("D"));
        using var multiple = CreateClient(
            _factory,
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));

        foreach (var client in new[] { malformed, empty, multiple })
        {
            var response = await client.GetAsync("/api/v1/hospitals");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = System.Text.Json.JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            Assert.Equal("Invalid tenant context", problem.RootElement.GetProperty("title").GetString());
        }
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

    [Theory]
    [InlineData(TenantRoles.HospitalAdmin, HttpStatusCode.Created, HttpStatusCode.NoContent)]
    [InlineData(TenantRoles.FacilityManager, HttpStatusCode.Created, HttpStatusCode.Forbidden)]
    [InlineData(TenantRoles.FacilityViewer, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(TenantRoles.PatientRegistrar, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(TenantRoles.ClinicalUser, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task PersistedRole_EnforcesFacilityReadWriteDeleteMatrixWithoutDeniedMutations(
        string role,
        HttpStatusCode expectedCreateStatus,
        HttpStatusCode expectedDeleteStatus)
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin, subject: "facility-matrix-admin");
        await _factory.SeedTenantMembershipAsync(
            tenantId, role, subject: "facility-matrix-member");

        using var administrator = CreateAuthenticatedClient(tenantId, "facility-matrix-admin");
        using var member = CreateAuthenticatedClient(tenantId, "facility-matrix-member");

        var baselineCreate = await administrator.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Baseline Hospital", code = "BASELINE" });
        Assert.Equal(HttpStatusCode.Created, baselineCreate.StatusCode);
        var baseline = await baselineCreate.Content.ReadFromJsonAsync<HospitalResponse>();
        Assert.NotNull(baseline);

        var read = await member.GetAsync("/api/v1/hospitals");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var create = await member.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Role Matrix Hospital", code = "ROLE-MATRIX" });
        Assert.Equal(expectedCreateStatus, create.StatusCode);

        var delete = await member.DeleteAsync($"/api/v1/hospitals/{baseline!.Id:D}");
        Assert.Equal(expectedDeleteStatus, delete.StatusCode);

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var remainingHospitals = await db.Hospitals.AsNoTracking().CountAsync();
        var expectedRemaining = 1
            + (expectedCreateStatus == HttpStatusCode.Created ? 1 : 0)
            - (expectedDeleteStatus == HttpStatusCode.NoContent ? 1 : 0);
        Assert.Equal(expectedRemaining, remainingHospitals);
    }

    private HttpClient CreateAuthenticatedClient(Guid? tenantId, string subject)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                TestJwtTokenFactory.CreateToken(subject: subject));
        if (tenantId.HasValue)
        {
            client.DefaultRequestHeaders.Add(
                TenantContextMiddleware.HeaderName,
                tenantId.Value.ToString("D"));
        }

        return client;
    }

    [Fact]
    public async Task ExternalTokenPermissionAndRoleClaimsCannotEscalateTenantMembership()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.FacilityViewer);
        // Simulate claims emitted by the configured identity provider. Only the
        // HeapCare membership row is authoritative for tenant role/permissions.
        using var client = CreateAuthenticatedClient(
            tenantId,
            new System.Security.Claims.Claim(AuthorizationPolicies.PermissionClaim, TenantPermissions.FacilityWrite),
            new System.Security.Claims.Claim("role", TenantRoles.HospitalAdmin));

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Role escalation attempt", code = "ESCALATE" });

        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(Guid? tenantId, params System.Security.Claims.Claim[] externalClaims)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                TestJwtTokenFactory.CreateToken(additionalClaims: externalClaims));
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
