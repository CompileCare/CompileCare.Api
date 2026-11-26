using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class TenantMembershipApiTests
{
    private readonly FacilityApiFactory _factory;

    public TenantMembershipApiTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task InitialAdministrator_IsPlatformProvisionedAndOnlyOneBootstrapWins()
    {
        await _factory.SeedPlatformOperatorAsync();
        using var platformClient = CreateClient(TestJwtTokenFactory.Subject);

        var tenantId = await CreateTenantAsync(platformClient, "Bootstrap tenant", "bootstrap-tenant");

        using var firstClient = CreateClient(TestJwtTokenFactory.Subject);
        using var secondClient = CreateClient(TestJwtTokenFactory.Subject);

        var firstTask = firstClient.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/initial-administrator",
            new { issuer = TestJwtTokenFactory.Issuer, subject = "first-admin" });
        var secondTask = secondClient.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/initial-administrator",
            new { issuer = TestJwtTokenFactory.Issuer, subject = "second-admin" });

        using var firstResponse = await firstTask;
        using var secondResponse = await secondTask;

        var statuses = new[] { firstResponse.StatusCode, secondResponse.StatusCode };
        Assert.Equal(
            HttpStatusCode.Created,
            Assert.Single(statuses, status => status == HttpStatusCode.Created));
        Assert.Equal(
            HttpStatusCode.Conflict,
            Assert.Single(statuses, status => status == HttpStatusCode.Conflict));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var memberships = await db.TenantMemberships.IgnoreQueryFilters()
            .Where(value => value.TenantId == tenantId)
            .Include(value => value.Scopes)
            .ToListAsync();

        var membership = Assert.Single(memberships);
        Assert.Equal(TenantRoles.HospitalAdmin, membership.Role);
        Assert.True(membership.IsActive);
        var tenantScope = Assert.Single(membership.Scopes);
        Assert.Equal(OrganizationScopeTypes.Tenant, tenantScope.ScopeType);
        Assert.Null(tenantScope.HospitalId);
        Assert.Null(tenantScope.DepartmentId);

        var activate = await platformClient.PostAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/activate",
            content: null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        var retryBootstrap = await platformClient.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/initial-administrator",
            new { issuer = TestJwtTokenFactory.Issuer, subject = membership.Subject });
        Assert.Equal(HttpStatusCode.OK, retryBootstrap.StatusCode);
        var retriedMembership = await retryBootstrap.Content.ReadFromJsonAsync<MembershipResponse>();
        Assert.NotNull(retriedMembership);
        Assert.Equal(membership.Id, retriedMembership!.Id);

        using var winningAdminClient = CreateClient(membership.Subject, tenantId);
        var hospitalList = await winningAdminClient.GetAsync("/api/v1/hospitals");
        Assert.Equal(HttpStatusCode.OK, hospitalList.StatusCode);
    }

    [Fact]
    public async Task DepartmentScopedManager_CannotReadOrMutateOtherHospitalData()
    {
        await _factory.SeedPlatformOperatorAsync();
        using var platformClient = CreateClient(TestJwtTokenFactory.Subject);

        var tenantId = await CreateTenantAsync(platformClient, "Scope tenant", "scope-tenant");
        var initialAdminResponse = await platformClient.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/initial-administrator",
            new { issuer = TestJwtTokenFactory.Issuer, subject = "tenant-admin" });
        Assert.Equal(HttpStatusCode.Created, initialAdminResponse.StatusCode);

        var activate = await platformClient.PostAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/activate",
            content: null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        using var adminClient = CreateClient("tenant-admin", tenantId);

        var hospitalA = await CreateHospitalAsync(adminClient, "Hospital A", "HOSP-A");
        var hospitalB = await CreateHospitalAsync(adminClient, "Hospital B", "HOSP-B");
        var departmentA = await CreateDepartmentAsync(adminClient, hospitalA, "Cardiology A");
        var departmentB = await CreateDepartmentAsync(adminClient, hospitalB, "Cardiology B");

        var membershipResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/tenant/memberships",
            new
            {
                issuer = TestJwtTokenFactory.Issuer,
                subject = "department-manager",
                role = TenantRoles.FacilityManager,
                scopes = new[]
                {
                    new { scopeType = OrganizationScopeTypes.Department, departmentId = departmentA }
                }
            });
        Assert.Equal(HttpStatusCode.Created, membershipResponse.StatusCode);
        var membership = await membershipResponse.Content.ReadFromJsonAsync<MembershipResponse>();
        Assert.NotNull(membership);

        // Replacing the same scope must delete the old row before inserting its
        // replacement, rather than colliding with the filtered unique index.
        var sameScopeUpdate = await adminClient.PutAsJsonAsync(
            $"/api/v1/tenant/memberships/{membership!.Id:D}",
            new
            {
                role = TenantRoles.FacilityManager,
                scopes = new[]
                {
                    new { scopeType = OrganizationScopeTypes.Department, departmentId = departmentA }
                }
            });
        var sameScopeBody = await sameScopeUpdate.Content.ReadAsStringAsync();
        Assert.True(
            sameScopeUpdate.StatusCode == HttpStatusCode.OK,
            $"Scope update returned {(int)sameScopeUpdate.StatusCode}: {sameScopeBody}");

        var reassignResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/tenant/memberships",
            new
            {
                issuer = TestJwtTokenFactory.Issuer,
                subject = "scope-reassigned",
                role = TenantRoles.FacilityViewer,
                scopes = new[]
                {
                    new { scopeType = OrganizationScopeTypes.Department, departmentId = departmentA }
                }
            });
        Assert.Equal(HttpStatusCode.Created, reassignResponse.StatusCode);
        var reassignMembership = await reassignResponse.Content.ReadFromJsonAsync<MembershipResponse>();
        Assert.NotNull(reassignMembership);

        var reassignment = await adminClient.PutAsJsonAsync(
            $"/api/v1/tenant/memberships/{reassignMembership!.Id:D}",
            new
            {
                role = TenantRoles.FacilityViewer,
                scopes = new[]
                {
                    new { scopeType = OrganizationScopeTypes.Department, departmentId = departmentB }
                }
            });
        var reassignmentBody = await reassignment.Content.ReadAsStringAsync();
        Assert.True(
            reassignment.StatusCode == HttpStatusCode.OK,
            $"Department reassignment returned {(int)reassignment.StatusCode}: {reassignmentBody}");

        using (var verifyScope = _factory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
            var persistedScope = await verifyDb.TenantMembershipScopes.IgnoreQueryFilters()
                .SingleAsync(value => value.MembershipId == reassignMembership.Id);
            Assert.Equal(departmentB, persistedScope.DepartmentId);
            Assert.Equal(hospitalB, persistedScope.HospitalId);
        }

        using var managerClient = CreateClient("department-manager", tenantId);
        var hospitalList = await managerClient.GetFromJsonAsync<JsonElement>("/api/v1/hospitals");
        Assert.Equal(1, hospitalList.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            hospitalA,
            hospitalList.GetProperty("items")[0].GetProperty("id").GetGuid());

        var hiddenHospital = await managerClient.GetAsync($"/api/v1/hospitals/{hospitalB:D}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenHospital.StatusCode);

        var ownDepartments = await managerClient.GetFromJsonAsync<JsonElement>(
            $"/api/v1/hospitals/{hospitalA:D}/departments");
        Assert.Equal(1, ownDepartments.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            departmentA,
            ownDepartments.GetProperty("items")[0].GetProperty("id").GetGuid());

        var foreignDepartments = await managerClient.GetAsync(
            $"/api/v1/hospitals/{hospitalB:D}/departments");
        Assert.Equal(HttpStatusCode.NotFound, foreignDepartments.StatusCode);
        Assert.Equal(
            "application/problem+json",
            foreignDepartments.Content.Headers.ContentType?.MediaType);
        using var foreignDepartmentProblem =
            JsonDocument.Parse(await foreignDepartments.Content.ReadAsStringAsync());
        Assert.Equal(404, foreignDepartmentProblem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            "Resource not found",
            foreignDepartmentProblem.RootElement.GetProperty("title").GetString());

        var createWard = await managerClient.PostAsJsonAsync(
            $"/api/v1/departments/{departmentA:D}/wards",
            new { name = "Allowed ward" });
        Assert.Equal(HttpStatusCode.Created, createWard.StatusCode);

        var crossDepartmentWard = await managerClient.PostAsJsonAsync(
            $"/api/v1/departments/{departmentB:D}/wards",
            new { name = "Forbidden ward" });
        Assert.Equal(HttpStatusCode.NotFound, crossDepartmentWard.StatusCode);

        var modifyParentHospital = await managerClient.PutAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}",
            new { name = "Attempted parent edit", code = "HOSP-A" });
        Assert.Equal(HttpStatusCode.NotFound, modifyParentHospital.StatusCode);

        var createDepartmentAtParent = await managerClient.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}/departments",
            new { name = "Not allowed department", code = "NOPE" });
        Assert.Equal(HttpStatusCode.NotFound, createDepartmentAtParent.StatusCode);

        var managerListMemberships = await managerClient.GetAsync("/api/v1/tenant/memberships");
        Assert.Equal(HttpStatusCode.Forbidden, managerListMemberships.StatusCode);

        var deactivate = await adminClient.PostAsync(
            $"/api/v1/tenant/memberships/{membership!.Id:D}/deactivate",
            content: null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var inactiveRead = await managerClient.GetAsync("/api/v1/hospitals");
        Assert.Equal(HttpStatusCode.Forbidden, inactiveRead.StatusCode);

        var reactivate = await adminClient.PostAsync(
            $"/api/v1/tenant/memberships/{membership.Id:D}/reactivate",
            content: null);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);

        var activeRead = await managerClient.GetAsync("/api/v1/hospitals");
        Assert.Equal(HttpStatusCode.OK, activeRead.StatusCode);

        var initialAdminId = await GetInitialAdminIdAsync(tenantId);
        var selfDeactivate = await adminClient.PostAsync(
            $"/api/v1/tenant/memberships/{initialAdminId:D}/deactivate",
            content: null);
        Assert.Equal(HttpStatusCode.Conflict, selfDeactivate.StatusCode);

        var selfUpdate = await adminClient.PutAsJsonAsync(
            $"/api/v1/tenant/memberships/{initialAdminId:D}",
            new
            {
                role = TenantRoles.FacilityViewer,
                scopes = new[] { new { scopeType = OrganizationScopeTypes.Tenant } }
            });
        Assert.Equal(HttpStatusCode.Conflict, selfUpdate.StatusCode);
    }

    [Fact]
    public async Task MembershipCreation_RejectsUnknownOrOutOfTenantOrganizationScope()
    {
        await _factory.SeedPlatformOperatorAsync();
        using var platformClient = CreateClient(TestJwtTokenFactory.Subject);

        var tenantId = await CreateTenantAsync(platformClient, "Validation tenant", "validation-tenant");
        var initialAdmin = await platformClient.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/initial-administrator",
            new { issuer = TestJwtTokenFactory.Issuer, subject = "validation-admin" });
        Assert.Equal(HttpStatusCode.Created, initialAdmin.StatusCode);

        await platformClient.PostAsync($"/api/v1/platform/tenants/{tenantId:D}/activate", content: null);
        using var adminClient = CreateClient("validation-admin", tenantId);

        var unknownScope = await adminClient.PostAsJsonAsync(
            "/api/v1/tenant/memberships",
            new
            {
                issuer = TestJwtTokenFactory.Issuer,
                subject = "unknown-scope-user",
                role = TenantRoles.FacilityViewer,
                scopes = new[] { new { scopeType = "Global" } }
            });
        Assert.Equal(HttpStatusCode.BadRequest, unknownScope.StatusCode);

        var outOfTenantHospital = await adminClient.PostAsJsonAsync(
            "/api/v1/tenant/memberships",
            new
            {
                issuer = TestJwtTokenFactory.Issuer,
                subject = "foreign-hospital-user",
                role = TenantRoles.FacilityViewer,
                scopes = new[]
                {
                    new { scopeType = OrganizationScopeTypes.Hospital, hospitalId = Guid.NewGuid() }
                }
            });
        Assert.Equal(HttpStatusCode.NotFound, outOfTenantHospital.StatusCode);
    }

    private async Task<Guid> CreateTenantAsync(HttpClient platformClient, string name, string slug)
    {
        var response = await platformClient.PostAsJsonAsync(
            "/api/v1/platform/tenants",
            new { name, slug });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tenant = await response.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(tenant);
        return tenant!.Id;
    }

    private async Task<Guid> CreateHospitalAsync(HttpClient client, string name, string code)
    {
        var response = await client.PostAsJsonAsync("/api/v1/hospitals", new { name, code });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var hospital = await response.Content.ReadFromJsonAsync<HospitalResponse>();
        Assert.NotNull(hospital);
        return hospital!.Id;
    }

    private async Task<Guid> CreateDepartmentAsync(HttpClient client, Guid hospitalId, string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/departments",
            new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var department = await response.Content.ReadFromJsonAsync<DepartmentResponse>();
        Assert.NotNull(department);
        return department!.Id;
    }

    private async Task<Guid> GetInitialAdminIdAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var membership = await db.TenantMemberships.IgnoreQueryFilters()
            .SingleAsync(value => value.TenantId == tenantId &&
                                  value.Subject == "tenant-admin");
        return membership.Id;
    }

    private HttpClient CreateClient(string subject, Guid? tenantId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken(subject: subject));
        if (tenantId.HasValue)
        {
            client.DefaultRequestHeaders.Add(
                TenantContextMiddleware.HeaderName,
                tenantId.Value.ToString("D"));
        }
        return client;
    }

    private sealed record TenantResponse(Guid Id, string Name, string Slug, string Status);
    private sealed record HospitalResponse(Guid Id, string Name, string? Code);
    private sealed record DepartmentResponse(Guid Id, Guid HospitalId, string Name, string? Code);
    private sealed record MembershipResponse(Guid Id, string Role, bool IsActive);
}
