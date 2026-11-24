using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class UserTenantContextApiTests
{
    private readonly FacilityApiFactory _factory;

    public UserTenantContextApiTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task CurrentUserTenantContexts_ReturnOnlyActiveMembershipsForValidatedIdentity_AndAreAudited()
    {
        var firstTenantId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        var inactiveTenantId = Guid.NewGuid();

        await _factory.SeedTenantMembershipAsync(firstTenantId, TenantRoles.HospitalAdmin);
        await _factory.SeedTenantMembershipAsync(secondTenantId, TenantRoles.PatientRegistrar);
        await _factory.SeedTenantMembershipAsync(
            inactiveTenantId, TenantRoles.HospitalAdmin, isActive: false);

        using var client = CreateAuthenticatedClient(
            new System.Security.Claims.Claim("role", "PlatformOperator"),
            new System.Security.Claims.Claim("permission", "tenant:all"));

        var response = await client.GetAsync("/api/v1/me/tenant-contexts?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<TenantContextPageResponse>();
        Assert.NotNull(page);
        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Contains(page.Items, value =>
            value.TenantId == firstTenantId && value.Role == TenantRoles.HospitalAdmin);
        Assert.Contains(page.Items, value =>
            value.TenantId == secondTenantId && value.Role == TenantRoles.PatientRegistrar);
        Assert.DoesNotContain(page.Items, value => value.TenantId == inactiveTenantId);
        Assert.All(page.Items, value =>
            Assert.Contains(value.Scopes, scope => scope.ScopeType == OrganizationScopeTypes.Tenant));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var audit = await db.AuditEvents.SingleAsync();

        Assert.Null(audit.TenantId);
        Assert.Null(audit.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
        Assert.Equal(TestJwtTokenFactory.Subject, audit.ActorSubject);
        Assert.Equal("ListCurrentUserTenantContexts", audit.Action);
        Assert.Equal("User context", audit.ResourceType);
        Assert.Equal("Succeeded", audit.Outcome);
        Assert.Equal((int)HttpStatusCode.OK, audit.StatusCode);
    }

    [Fact]
    public async Task AnonymousCurrentUserTenantContextRequest_IsAuditedAsDenied()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/me/tenant-contexts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var audit = await db.AuditEvents.SingleAsync();

        Assert.Null(audit.TenantId);
        Assert.Null(audit.RequestedTenantId);
        Assert.Null(audit.ActorIssuer);
        Assert.Null(audit.ActorSubject);
        Assert.Equal("ListCurrentUserTenantContexts", audit.Action);
        Assert.Equal("Denied", audit.Outcome);
        Assert.Equal((int)HttpStatusCode.Unauthorized, audit.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(params System.Security.Claims.Claim[] additionalClaims)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                TestJwtTokenFactory.CreateToken(additionalClaims: additionalClaims));
        return client;
    }

    private sealed record TenantContextPageResponse(
        IReadOnlyList<TenantContextResponse> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages);

    private sealed record TenantContextResponse(
        Guid TenantId,
        string TenantName,
        string TenantSlug,
        string Role,
        IReadOnlyList<OrganizationScope> Scopes);
}
