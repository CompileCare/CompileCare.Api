using System.Net;
using System.Security.Claims;
using CompileCare.Api.Security;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class TenantRegistryApiTests
{
    private readonly FacilityApiFactory _factory;

    public TenantRegistryApiTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task TenantRegistry_RequiresPersistedPlatformOperator()
    {
        using var anonymous = _factory.CreateClient();
        var anonymousResponse = await anonymous.GetAsync("/api/v1/platform/tenants");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        // A role and permission claim from the external identity provider must not
        // grant platform access without a matching persisted platform-operator record.
        using var authenticatedNonOperator = CreateAuthenticatedClient(
            additionalClaims:
            [
                new Claim("role", "PlatformOperator"),
                new Claim(AuthorizationPolicies.PermissionClaim, "platform:tenant:manage")
            ]);
        var deniedResponse = await authenticatedNonOperator.GetAsync("/api/v1/platform/tenants");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
    }

    [Fact]
    public async Task PlatformOperator_CanCreateListAndReadTenant()
    {
        await _factory.SeedPlatformOperatorAsync();
        using var client = CreateAuthenticatedClient();

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/platform/tenants",
            new { name = "  Example Hospital Group  ", slug = "  Example-Group " });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(created);
        Assert.Equal("Example Hospital Group", created!.Name);
        Assert.Equal("example-group", created.Slug);
        Assert.Equal("Provisioning", created.Status);
        Assert.True(created.Id != Guid.Empty);

        var getResponse = await client.GetAsync($"/api/v1/platform/tenants/{created.Id:D}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/platform/tenants?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<TenantPageResponse>();
        Assert.NotNull(page);
        Assert.Contains(page!.Items, tenant => tenant.Id == created.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var audit = await db.AuditEvents
            .Where(value => value.Action == "CreatePlatformTenant")
            .SingleAsync();
        Assert.Null(audit.TenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
        Assert.Equal(TestJwtTokenFactory.Subject, audit.ActorSubject);
        Assert.Equal("Succeeded", audit.Outcome);
    }

    [Fact]
    public async Task TenantLifecycle_ControlsTenantScopedAccessAndClosedIsTerminal()
    {
        await _factory.SeedPlatformOperatorAsync();
        using var platformClient = CreateAuthenticatedClient();

        var createResponse = await platformClient.PostAsJsonAsync(
            "/api/v1/platform/tenants",
            new { name = "Lifecycle Tenant", slug = "lifecycle-tenant" });
        var tenant = await createResponse.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(tenant);

        await _factory.SeedTenantMembershipAsync(tenant!.Id, TenantRoles.HospitalAdmin);
        using var tenantClient = CreateAuthenticatedClient(tenant.Id);
        var beforeActivation = await tenantClient.GetAsync("/api/v1/hospitals");
        Assert.Equal(HttpStatusCode.Forbidden, beforeActivation.StatusCode);

        var activate = await platformClient.PostAsync(
            $"/api/v1/platform/tenants/{tenant.Id:D}/activate", content: null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var activeAccess = await tenantClient.GetAsync("/api/v1/hospitals");
        Assert.Equal(HttpStatusCode.OK, activeAccess.StatusCode);

        var suspend = await platformClient.PostAsync(
            $"/api/v1/platform/tenants/{tenant.Id:D}/suspend", content: null);
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        var suspendedAccess = await tenantClient.GetAsync("/api/v1/hospitals");
        Assert.Equal(HttpStatusCode.Forbidden, suspendedAccess.StatusCode);

        var reactivate = await platformClient.PostAsync(
            $"/api/v1/platform/tenants/{tenant.Id:D}/activate", content: null);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        var reactivatedAccess = await tenantClient.GetAsync("/api/v1/hospitals");
        Assert.Equal(HttpStatusCode.OK, reactivatedAccess.StatusCode);

        var close = await platformClient.PostAsync(
            $"/api/v1/platform/tenants/{tenant.Id:D}/close", content: null);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        var closedAccess = await tenantClient.GetAsync("/api/v1/hospitals");
        Assert.Equal(HttpStatusCode.Forbidden, closedAccess.StatusCode);

        var reopen = await platformClient.PostAsync(
            $"/api/v1/platform/tenants/{tenant.Id:D}/activate", content: null);
        Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode);
    }

    [Fact]
    public async Task TenantRegistry_RejectsInvalidAndDuplicateSlugs()
    {
        await _factory.SeedPlatformOperatorAsync();
        using var client = CreateAuthenticatedClient();

        var invalidSlug = await client.PostAsJsonAsync(
            "/api/v1/platform/tenants",
            new { name = "Invalid Slug", slug = "not valid" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidSlug.StatusCode);

        var first = await client.PostAsJsonAsync(
            "/api/v1/platform/tenants",
            new { name = "First", slug = "duplicate-slug" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsJsonAsync(
            "/api/v1/platform/tenants",
            new { name = "Second", slug = "duplicate-slug" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }


    [Fact]
    public async Task InitialAdministratorProvisioning_IsAuditedAsPrivilegedMembershipChange()
    {
        await _factory.SeedPlatformOperatorAsync();
        using var client = CreateAuthenticatedClient();

        var createTenant = await client.PostAsJsonAsync(
            "/api/v1/platform/tenants",
            new { name = "Membership Audit Tenant", slug = "membership-audit-tenant" });
        Assert.Equal(HttpStatusCode.Created, createTenant.StatusCode);
        var tenant = await createTenant.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(tenant);

        var provision = await client.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{tenant!.Id:D}/initial-administrator",
            new { issuer = TestJwtTokenFactory.Issuer, subject = "initial-admin-subject" });

        Assert.Equal(HttpStatusCode.Created, provision.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var audit = await db.AuditEvents
            .SingleAsync(value => value.Action == "CreateInitialTenantAdministrator");

        Assert.Equal(tenant.Id, audit.TenantId);
        Assert.Null(audit.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
        Assert.Equal(TestJwtTokenFactory.Subject, audit.ActorSubject);
        Assert.Equal("Platform tenant memberships", audit.ResourceType);
        Assert.Equal("Succeeded", audit.Outcome);
        Assert.Equal((int)HttpStatusCode.Created, audit.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(
        Guid? tenantId = null,
        params Claim[] additionalClaims)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                TestJwtTokenFactory.CreateToken(additionalClaims: additionalClaims));

        if (tenantId.HasValue)
        {
            client.DefaultRequestHeaders.Add(
                TenantContextMiddleware.HeaderName,
                tenantId.Value.ToString("D"));
        }

        return client;
    }

    private sealed record TenantResponse(Guid Id, string Name, string Slug, string Status);
    private sealed record TenantPageResponse(
        IReadOnlyList<TenantResponse> Items, int Page, int PageSize, int TotalCount);
}
