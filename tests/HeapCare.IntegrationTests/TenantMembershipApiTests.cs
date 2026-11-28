using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HeapCare.Api.Middleware;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeapCare.IntegrationTests;

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
    public async Task GlobalIdentity_IsSharedAcrossTenantMembershipsAndPlatformOperator()
    {
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();

        await _factory.SeedTenantMembershipAsync(firstTenant, TenantRoles.HospitalAdmin);
        await _factory.SeedTenantMembershipAsync(secondTenant, TenantRoles.ClinicalUser);
        await _factory.SeedPlatformOperatorAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        var memberships = await db.TenantMemberships.IgnoreQueryFilters()
            .Where(value => value.Subject == TestJwtTokenFactory.Subject)
            .OrderBy(value => value.TenantId)
            .ToListAsync();
        var membershipA = Assert.Single(memberships, value => value.TenantId == firstTenant);
        var membershipB = Assert.Single(memberships, value => value.TenantId == secondTenant);
        var platformOperator = await db.PlatformOperators.SingleAsync(
            value => value.Issuer == TestJwtTokenFactory.Issuer &&
                     value.Subject == TestJwtTokenFactory.Subject);

        Assert.NotEqual(Guid.Empty, membershipA.UserIdentityId);
        Assert.Equal(membershipA.UserIdentityId, membershipB.UserIdentityId);
        Assert.Equal(membershipA.UserIdentityId, platformOperator.UserIdentityId);

        var identityCount = await db.UserIdentities.CountAsync(
            value => value.Issuer == TestJwtTokenFactory.Issuer &&
                     value.Subject == TestJwtTokenFactory.Subject);
        Assert.Equal(1, identityCount);

        // Issuer is part of the identity key. The same subject from another
        // OIDC authority must remain a distinct global identity.
        var otherIssuerIdentity = await UserIdentityResolver.ResolveAsync(
            db,
            "https://other-issuer.example",
            TestJwtTokenFactory.Subject,
            CancellationToken.None);
        Assert.NotEqual(membershipA.UserIdentityId, otherIssuerIdentity.Id);
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
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
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
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
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
    public async Task HospitalScopedAdministrator_ListsOnlyMembershipsWithinHospitalScope()
    {
        await _factory.SeedPlatformOperatorAsync();
        using var platformClient = CreateClient(TestJwtTokenFactory.Subject);

        var tenantId = await CreateTenantAsync(
            platformClient, "Scoped membership list tenant", "scoped-membership-list");
        var initialAdmin = await platformClient.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/initial-administrator",
            new { issuer = TestJwtTokenFactory.Issuer, subject = "scoped-list-tenant-admin" });
        Assert.Equal(HttpStatusCode.Created, initialAdmin.StatusCode);

        var activate = await platformClient.PostAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/activate", content: null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        using var tenantAdmin = CreateClient("scoped-list-tenant-admin", tenantId);
        var hospitalA = await CreateHospitalAsync(tenantAdmin, "Scoped List Hospital A", "LIST-A");
        var hospitalB = await CreateHospitalAsync(tenantAdmin, "Scoped List Hospital B", "LIST-B");
        var departmentA = await CreateDepartmentAsync(tenantAdmin, hospitalA, "Scoped List Department A");

        async Task<Guid> CreateMembershipAsync(
            string subject,
            string role,
            object scope)
        {
            var response = await tenantAdmin.PostAsJsonAsync(
                "/api/v1/tenant/memberships",
                new
                {
                    issuer = TestJwtTokenFactory.Issuer,
                    subject,
                    role,
                    scopes = new[] { scope }
                });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var membership = await response.Content.ReadFromJsonAsync<MembershipResponse>();
            Assert.NotNull(membership);
            return membership!.Id;
        }

        var hospitalMemberId = await CreateMembershipAsync(
            "scoped-list-hospital-member",
            TenantRoles.FacilityViewer,
            new { scopeType = OrganizationScopeTypes.Hospital, hospitalId = hospitalA });
        var departmentMemberId = await CreateMembershipAsync(
            "scoped-list-department-member",
            TenantRoles.ClinicalUser,
            new { scopeType = OrganizationScopeTypes.Department, departmentId = departmentA });
        var otherHospitalMemberId = await CreateMembershipAsync(
            "scoped-list-other-hospital-member",
            TenantRoles.FacilityViewer,
            new { scopeType = OrganizationScopeTypes.Hospital, hospitalId = hospitalB });
        var tenantWideMemberId = await CreateMembershipAsync(
            "scoped-list-tenant-wide-member",
            TenantRoles.HospitalAdmin,
            new { scopeType = OrganizationScopeTypes.Tenant });

        var scopedAdminResponse = await tenantAdmin.PostAsJsonAsync(
            "/api/v1/tenant/memberships",
            new
            {
                issuer = TestJwtTokenFactory.Issuer,
                subject = "scoped-list-hospital-admin",
                role = TenantRoles.HospitalAdmin,
                scopes = new[]
                {
                    new { scopeType = OrganizationScopeTypes.Hospital, hospitalId = hospitalA }
                }
            });
        Assert.Equal(HttpStatusCode.Created, scopedAdminResponse.StatusCode);
        var scopedAdmin = await scopedAdminResponse.Content.ReadFromJsonAsync<MembershipResponse>();
        Assert.NotNull(scopedAdmin);

        using var hospitalAdmin = CreateClient("scoped-list-hospital-admin", tenantId);
        var response = await hospitalAdmin.GetAsync("/api/v1/tenant/memberships?page=1&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var items = root.GetProperty("items").EnumerateArray().ToArray();
        var returnedIds = items.Select(item => item.GetProperty("id").GetGuid()).ToHashSet();

        Assert.Equal(3, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, items.Length);
        Assert.Contains(hospitalMemberId, returnedIds);
        Assert.Contains(departmentMemberId, returnedIds);
        Assert.Contains(scopedAdmin!.Id, returnedIds);
        Assert.DoesNotContain(otherHospitalMemberId, returnedIds);
        Assert.DoesNotContain(tenantWideMemberId, returnedIds);
    }

    [Fact]
    public async Task DepartmentScopedAdministrator_ListsOnlyMembershipsWithinDepartmentScope()
    {
        await _factory.SeedPlatformOperatorAsync();
        using var platformClient = CreateClient(TestJwtTokenFactory.Subject);

        var tenantId = await CreateTenantAsync(
            platformClient, "Department membership list tenant", "department-membership-list");
        var initialAdmin = await platformClient.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/initial-administrator",
            new { issuer = TestJwtTokenFactory.Issuer, subject = "department-list-tenant-admin" });
        Assert.Equal(HttpStatusCode.Created, initialAdmin.StatusCode);
        var activate = await platformClient.PostAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/activate", content: null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        using var tenantAdmin = CreateClient("department-list-tenant-admin", tenantId);
        var hospital = await CreateHospitalAsync(tenantAdmin, "Department List Hospital", "DEPT-LIST");
        var departmentA = await CreateDepartmentAsync(tenantAdmin, hospital, "Department A");
        var departmentB = await CreateDepartmentAsync(tenantAdmin, hospital, "Department B");

        async Task<Guid> CreateMembershipAsync(string subject, string role, object scope)
        {
            var response = await tenantAdmin.PostAsJsonAsync(
                "/api/v1/tenant/memberships",
                new
                {
                    issuer = TestJwtTokenFactory.Issuer,
                    subject,
                    role,
                    scopes = new[] { scope }
                });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var membership = await response.Content.ReadFromJsonAsync<MembershipResponse>();
            Assert.NotNull(membership);
            return membership!.Id;
        }

        var ownDepartmentMemberId = await CreateMembershipAsync(
            "department-list-own-member",
            TenantRoles.ClinicalUser,
            new { scopeType = OrganizationScopeTypes.Department, departmentId = departmentA });
        var otherDepartmentMemberId = await CreateMembershipAsync(
            "department-list-other-member",
            TenantRoles.ClinicalUser,
            new { scopeType = OrganizationScopeTypes.Department, departmentId = departmentB });
        var hospitalWideMemberId = await CreateMembershipAsync(
            "department-list-hospital-member",
            TenantRoles.FacilityViewer,
            new { scopeType = OrganizationScopeTypes.Hospital, hospitalId = hospital });

        var scopedAdminId = await CreateMembershipAsync(
            "department-list-scoped-admin",
            TenantRoles.HospitalAdmin,
            new { scopeType = OrganizationScopeTypes.Department, departmentId = departmentA });

        using var departmentAdmin = CreateClient("department-list-scoped-admin", tenantId);
        var response = await departmentAdmin.GetAsync("/api/v1/tenant/memberships?page=1&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var items = root.GetProperty("items").EnumerateArray().ToArray();
        var returnedIds = items.Select(item => item.GetProperty("id").GetGuid()).ToHashSet();

        Assert.Equal(2, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, items.Length);
        Assert.Contains(ownDepartmentMemberId, returnedIds);
        Assert.Contains(scopedAdminId, returnedIds);
        Assert.DoesNotContain(otherDepartmentMemberId, returnedIds);
        Assert.DoesNotContain(hospitalWideMemberId, returnedIds);
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

    [Fact]
    public async Task MembershipCreation_RejectsDepartmentPairedWithDifferentHospitalAndPersistsCanonicalParent()
    {
        await _factory.SeedPlatformOperatorAsync();
        using var platformClient = CreateClient(TestJwtTokenFactory.Subject);

        var tenantId = await CreateTenantAsync(
            platformClient, "Department scope validation tenant", "department-scope-validation");
        var initialAdmin = await platformClient.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/initial-administrator",
            new { issuer = TestJwtTokenFactory.Issuer, subject = "department-scope-admin" });
        Assert.Equal(HttpStatusCode.Created, initialAdmin.StatusCode);

        var activate = await platformClient.PostAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/activate", content: null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        using var adminClient = CreateClient("department-scope-admin", tenantId);
        var hospitalA = await CreateHospitalAsync(adminClient, "Scope Hospital A", "SCOPE-A");
        var hospitalB = await CreateHospitalAsync(adminClient, "Scope Hospital B", "SCOPE-B");
        var departmentA = await CreateDepartmentAsync(adminClient, hospitalA, "Department A");
        var departmentB = await CreateDepartmentAsync(adminClient, hospitalB, "Department B");

        var mismatchedParent = await adminClient.PostAsJsonAsync(
            "/api/v1/tenant/memberships",
            new
            {
                issuer = TestJwtTokenFactory.Issuer,
                subject = "mismatched-department-scope",
                role = TenantRoles.ClinicalUser,
                scopes = new[]
                {
                    new
                    {
                        scopeType = OrganizationScopeTypes.Department,
                        hospitalId = hospitalA,
                        departmentId = departmentB
                    }
                }
            });
        Assert.Equal(HttpStatusCode.BadRequest, mismatchedParent.StatusCode);

        var canonicalDepartmentScope = await adminClient.PostAsJsonAsync(
            "/api/v1/tenant/memberships",
            new
            {
                issuer = TestJwtTokenFactory.Issuer,
                subject = "canonical-department-scope",
                role = TenantRoles.ClinicalUser,
                scopes = new[]
                {
                    new
                    {
                        scopeType = OrganizationScopeTypes.Department,
                        departmentId = departmentA
                    }
                }
            });

        Assert.Equal(HttpStatusCode.Created, canonicalDepartmentScope.StatusCode);
        using var document = JsonDocument.Parse(
            await canonicalDepartmentScope.Content.ReadAsStringAsync());
        var scope = Assert.Single(document.RootElement.GetProperty("scopes").EnumerateArray());
        Assert.Equal(OrganizationScopeTypes.Department,
            scope.GetProperty("scopeType").GetString());
        Assert.Equal(hospitalA, scope.GetProperty("hospitalId").GetGuid());
        Assert.Equal(departmentA, scope.GetProperty("departmentId").GetGuid());
    }

    [Fact]
    public async Task MembershipRoleScopeAndActivationChanges_AreAuditedAsMetadata()
    {
        await _factory.SeedPlatformOperatorAsync();
        using var platformClient = CreateClient(TestJwtTokenFactory.Subject);

        var tenantId = await CreateTenantAsync(
            platformClient, "Membership audit tenant", "membership-audit-tenant");
        var provision = await platformClient.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/initial-administrator",
            new { issuer = TestJwtTokenFactory.Issuer, subject = "membership-audit-admin" });
        Assert.Equal(HttpStatusCode.Created, provision.StatusCode);

        var activateTenant = await platformClient.PostAsync(
            $"/api/v1/platform/tenants/{tenantId:D}/activate",
            content: null);
        Assert.Equal(HttpStatusCode.OK, activateTenant.StatusCode);

        using var adminClient = CreateClient("membership-audit-admin", tenantId);
        var hospitalId = await CreateHospitalAsync(
            adminClient, "Membership audit hospital", "MEM-AUDIT");
        var departmentId = await CreateDepartmentAsync(
            adminClient, hospitalId, "Membership audit department");

        var createMembership = await adminClient.PostAsJsonAsync(
            "/api/v1/tenant/memberships",
            new
            {
                issuer = TestJwtTokenFactory.Issuer,
                subject = "membership-audit-target",
                role = TenantRoles.FacilityViewer,
                scopes = new[]
                {
                    new
                    {
                        scopeType = OrganizationScopeTypes.Department,
                        departmentId
                    }
                }
            });
        Assert.Equal(HttpStatusCode.Created, createMembership.StatusCode);
        var targetMembership = await createMembership.Content.ReadFromJsonAsync<MembershipResponse>();
        Assert.NotNull(targetMembership);

        using (var baselineScope = _factory.Services.CreateScope())
        {
            baselineScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var baselineDb = baselineScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
            var existingAuditIds = await baselineDb.AuditEvents.IgnoreQueryFilters()
                .Where(value => value.TenantId == tenantId &&
                                value.ActorSubject == "membership-audit-admin")
                .Select(value => value.Id)
                .ToListAsync();

            var update = await adminClient.PutAsJsonAsync(
                $"/api/v1/tenant/memberships/{targetMembership!.Id:D}",
                new
                {
                    role = TenantRoles.FacilityManager,
                    scopes = new[]
                    {
                        new
                        {
                            scopeType = OrganizationScopeTypes.Hospital,
                            hospitalId
                        }
                    }
                });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);

            var deactivate = await adminClient.PostAsync(
                $"/api/v1/tenant/memberships/{targetMembership.Id:D}/deactivate",
                content: null);
            Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

            var reactivate = await adminClient.PostAsync(
                $"/api/v1/tenant/memberships/{targetMembership.Id:D}/reactivate",
                content: null);
            Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);

            var adminMembershipId = await baselineDb.TenantMemberships.IgnoreQueryFilters()
                .Where(value => value.TenantId == tenantId &&
                                value.Subject == "membership-audit-admin")
                .Select(value => value.Id)
                .SingleAsync();
            var deniedSelfUpdate = await adminClient.PutAsJsonAsync(
                $"/api/v1/tenant/memberships/{adminMembershipId:D}",
                new
                {
                    role = TenantRoles.FacilityViewer,
                    scopes = new[]
                    {
                        new { scopeType = OrganizationScopeTypes.Tenant }
                    }
                });
            Assert.Equal(HttpStatusCode.Conflict, deniedSelfUpdate.StatusCode);

            // Read the audit rows in the same tenant context and exclude fixture
            // activity so this assertion is deterministic even as setup evolves.
            var auditEvents = await baselineDb.AuditEvents.IgnoreQueryFilters()
                .Where(value => value.TenantId == tenantId &&
                                value.ActorSubject == "membership-audit-admin" &&
                                !existingAuditIds.Contains(value.Id))
                .ToListAsync();

            Assert.Equal(4, auditEvents.Count);
            var targetAudits = auditEvents
                .Where(value => value.ResourceId == targetMembership.Id)
                .ToList();
            Assert.Equal(3, targetAudits.Count);

            var updateAudit = Assert.Single(
                targetAudits, value => value.Action == "UpdateTenantMembership");
            var deactivateAudit = Assert.Single(
                targetAudits, value => value.Action == "DeactivateTenantMembership");
            var reactivateAudit = Assert.Single(
                targetAudits, value => value.Action == "ReactivateTenantMembership");

            Assert.Equal("PUT", updateAudit.HttpMethod);
            Assert.Equal("POST", deactivateAudit.HttpMethod);
            Assert.Equal("POST", reactivateAudit.HttpMethod);

            Assert.All(targetAudits, audit =>
            {
                Assert.Equal(tenantId, audit.TenantId);
                Assert.Equal(tenantId, audit.RequestedTenantId);
                Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
                Assert.Equal("membership-audit-admin", audit.ActorSubject);
                Assert.Equal("Tenant memberships", audit.ResourceType);
                Assert.Equal(targetMembership.Id, audit.ResourceId);
                Assert.Equal("Succeeded", audit.Outcome);
                Assert.Equal((int)HttpStatusCode.OK, audit.StatusCode);
            });

            var selfUpdateAudit = Assert.Single(
                auditEvents, value => value.ResourceId == adminMembershipId);
            Assert.Equal("UpdateTenantMembership", selfUpdateAudit.Action);
            Assert.Equal("PUT", selfUpdateAudit.HttpMethod);
            Assert.Equal(tenantId, selfUpdateAudit.TenantId);
            Assert.Equal(tenantId, selfUpdateAudit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, selfUpdateAudit.ActorIssuer);
            Assert.Equal("membership-audit-admin", selfUpdateAudit.ActorSubject);
            Assert.Equal("Tenant memberships", selfUpdateAudit.ResourceType);
            Assert.Equal("Rejected", selfUpdateAudit.Outcome);
            Assert.Equal((int)HttpStatusCode.Conflict, selfUpdateAudit.StatusCode);

            var persistedAdmin = await baselineDb.TenantMemberships.IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(value => value.Id == adminMembershipId);
            Assert.Equal(TenantRoles.HospitalAdmin, persistedAdmin.Role);
            Assert.True(persistedAdmin.IsActive);
        }
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
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
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
