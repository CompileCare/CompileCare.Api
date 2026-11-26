using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Staff;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class StaffManagementApiTests
{
    private readonly FacilityApiFactory _factory;

    public StaffManagementApiTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task HospitalAdmin_CanManageStaffProfilesAndEffectiveDatedAssignments()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId) = await SeedHospitalAndDepartmentAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        using var client = CreateClient(tenantId);

        var create = await client.PostAsJsonAsync("/api/v1/staff", new StaffProfileCreateInput(
            " emp-001 ", "Asha", "R.", "Patil", "Consultant", hospitalId, departmentId,
            new DateOnly(2026, 1, 1)));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var profile = await create.Content.ReadFromJsonAsync<StaffProfileDto>();
        Assert.NotNull(profile);
        Assert.Equal("EMP-001", profile!.StaffCode);
        Assert.Equal("Asha", profile.GivenName);
        Assert.Equal("Consultant", profile.ProviderDesignation);
        var assignment = Assert.Single(profile.Assignments);
        Assert.Equal(hospitalId, assignment.HospitalId);
        Assert.Equal(departmentId, assignment.DepartmentId);
        Assert.Equal(new DateOnly(2026, 1, 1), assignment.EffectiveFrom);

        var listed = await client.GetFromJsonAsync<StaffPage>("/api/v1/staff?page=1&pageSize=10");
        Assert.NotNull(listed);
        Assert.Equal(1, listed!.TotalCount);
        Assert.Equal(profile.Id, Assert.Single(listed.Items).Id);

        var duplicate = await client.PostAsJsonAsync("/api/v1/staff", new StaffProfileCreateInput(
            "EMP-001", "Another", null, "Person", null, hospitalId, null, new DateOnly(2026, 2, 1)));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var update = await client.PutAsJsonAsync($"/api/v1/staff/{profile.Id:D}",
            new StaffProfileUpdateInput("Asha", "R.", "Patil", "Senior Consultant"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<StaffProfileDto>();
        Assert.Equal("Senior Consultant", updated!.ProviderDesignation);

        var secondAssignment = await client.PostAsJsonAsync(
            $"/api/v1/staff/{profile.Id:D}/assignments",
            new StaffAssignmentInput(hospitalId, null, new DateOnly(2026, 3, 1)));
        Assert.Equal(HttpStatusCode.Created, secondAssignment.StatusCode);

        var duplicateAssignment = await client.PostAsJsonAsync(
            $"/api/v1/staff/{profile.Id:D}/assignments",
            new StaffAssignmentInput(hospitalId, null, new DateOnly(2026, 4, 1)));
        Assert.Equal(HttpStatusCode.Conflict, duplicateAssignment.StatusCode);

        var assignmentDto = await secondAssignment.Content.ReadFromJsonAsync<StaffAssignmentDto>();
        var end = await client.PostAsJsonAsync(
            $"/api/v1/staff/{profile.Id:D}/assignments/{assignmentDto!.Id:D}/end",
            new StaffAssignmentEndInput(new DateOnly(2026, 8, 31)));
        Assert.Equal(HttpStatusCode.OK, end.StatusCode);
        var ended = await end.Content.ReadFromJsonAsync<StaffAssignmentDto>();
        Assert.Equal(new DateOnly(2026, 8, 31), ended!.EffectiveTo);
        Assert.False(ended.IsActive);
    }

    [Fact]
    public async Task DepartmentScopedAdmin_CannotCreateHospitalWideAssignment()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId) = await SeedHospitalAndDepartmentAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, departmentId)]);
        using var client = CreateClient(tenantId);

        var outsideScope = await client.PostAsJsonAsync("/api/v1/staff", new StaffProfileCreateInput(
            "DEPT-001", "Ravi", null, "Deshmukh", null, hospitalId, null, new DateOnly(2026, 1, 1)));
        Assert.Equal(HttpStatusCode.NotFound, outsideScope.StatusCode);

        var withinScope = await client.PostAsJsonAsync("/api/v1/staff", new StaffProfileCreateInput(
            "DEPT-001", "Ravi", null, "Deshmukh", null, hospitalId, departmentId, new DateOnly(2026, 1, 1)));
        Assert.Equal(HttpStatusCode.Created, withinScope.StatusCode);
    }

    [Fact]
    public async Task FacilityManager_DoesNotImplicitlyReceiveStaffPermissions()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, _) = await SeedHospitalAndDepartmentAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.FacilityManager,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        using var client = CreateClient(tenantId);

        var response = await client.GetAsync("/api/v1/staff");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HospitalScopedAdmin_CanReadButCannotManageProfileWithAssignmentsOutsideScope()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalA, departmentA) = await SeedHospitalAndDepartmentAsync(tenantId);
        var (hospitalB, _) = await SeedHospitalAndDepartmentAsync(tenantId);

        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Tenant)],
            subject: "tenant-wide-staff-admin");
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalA)],
            subject: "hospital-a-staff-admin");

        using var tenantAdmin = CreateClient(tenantId, "tenant-wide-staff-admin");
        using var hospitalAdmin = CreateClient(tenantId, "hospital-a-staff-admin");

        var create = await tenantAdmin.PostAsJsonAsync("/api/v1/staff", new StaffProfileCreateInput(
            "MULTI-001", "Mira", null, "Shah", "Consultant", hospitalA, departmentA,
            new DateOnly(2026, 1, 1)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var profile = await create.Content.ReadFromJsonAsync<StaffProfileDto>();
        Assert.NotNull(profile);

        var secondAssignment = await tenantAdmin.PostAsJsonAsync(
            $"/api/v1/staff/{profile!.Id:D}/assignments",
            new StaffAssignmentInput(hospitalB, null, new DateOnly(2026, 2, 1)));
        Assert.Equal(HttpStatusCode.Created, secondAssignment.StatusCode);

        var visiblePage = await hospitalAdmin.GetFromJsonAsync<StaffPage>("/api/v1/staff?page=1&pageSize=10");
        Assert.NotNull(visiblePage);
        var visibleProfile = Assert.Single(visiblePage!.Items);
        Assert.Equal(profile.Id, visibleProfile.Id);
        Assert.False(visibleProfile.CanManageWithinScope);
        Assert.Single(visibleProfile.Assignments);
        Assert.Equal(hospitalA, visibleProfile.Assignments[0].HospitalId);

        var update = await hospitalAdmin.PutAsJsonAsync(
            $"/api/v1/staff/{profile.Id:D}",
            new StaffProfileUpdateInput("Mira", null, "Shah", "Unauthorized update"));
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }

    [Fact]
    public async Task CrossTenantAdminCannotDiscoverReadOrMutateStaffProfilesAndAssignments()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (hospitalA, departmentA) = await SeedHospitalAndDepartmentAsync(tenantA);
        var (hospitalB, _) = await SeedHospitalAndDepartmentAsync(tenantB);

        await _factory.SeedTenantMembershipAsync(
            tenantA, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalA)]);
        await _factory.SeedTenantMembershipAsync(
            tenantB, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalB)]);

        using var adminA = CreateClient(tenantA);
        using var adminB = CreateClient(tenantB);

        var create = await adminA.PostAsJsonAsync("/api/v1/staff", new StaffProfileCreateInput(
            "EMP-A-001", "Asha", "R.", "Patil", "Consultant", hospitalA, departmentA,
            new DateOnly(2026, 1, 1)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var profile = await create.Content.ReadFromJsonAsync<StaffProfileDto>();
        Assert.NotNull(profile);
        var assignment = Assert.Single(profile!.Assignments);

        var search = await adminB.GetAsync("/api/v1/staff?query=Asha&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var searchBody = await search.Content.ReadAsStringAsync();
        Assert.DoesNotContain("EMP-A-001", searchBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Asha", searchBody, StringComparison.Ordinal);
        var page = await search.Content.ReadFromJsonAsync<StaffPage>();
        Assert.NotNull(page);
        Assert.Empty(page!.Items);

        // Capture a per-test audit watermark after the successful tenant-B search.
        // The shared integration database may contain earlier audit rows for the same test subject.
        using var auditBaselineScope = _factory.Services.CreateScope();
        auditBaselineScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantB);
        var auditBaselineDb = auditBaselineScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var existingAuditIds = await auditBaselineDb.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorSubject == TestJwtTokenFactory.Subject)
            .Select(audit => audit.Id)
            .ToListAsync();

        var attempts = new[]
        {
            await adminB.GetAsync($"/api/v1/staff/{profile.Id:D}"),
            await adminB.PutAsJsonAsync(
                $"/api/v1/staff/{profile.Id:D}",
                new StaffProfileUpdateInput("Asha", "R.", "Patil", "Unauthorized update")),
            await adminB.PostAsync($"/api/v1/staff/{profile.Id:D}/deactivate", null),
            await adminB.PostAsync($"/api/v1/staff/{profile.Id:D}/reactivate", null),
            await adminB.PostAsJsonAsync(
                $"/api/v1/staff/{profile.Id:D}/assignments",
                new StaffAssignmentInput(hospitalA, null, new DateOnly(2026, 3, 1))),
            await adminB.PostAsJsonAsync(
                $"/api/v1/staff/{profile.Id:D}/assignments/{assignment.Id:D}/end",
                new StaffAssignmentEndInput(new DateOnly(2026, 8, 31)))
        };

        Assert.All(attempts, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var deniedBodies = await Task.WhenAll(attempts.Select(response => response.Content.ReadAsStringAsync()));
        Assert.All(deniedBodies, body =>
        {
            Assert.DoesNotContain("EMP-A-001", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Asha", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Patil", body, StringComparison.Ordinal);
        });

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantB);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var auditEvents = await db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorSubject == TestJwtTokenFactory.Subject &&
                            !existingAuditIds.Contains(audit.Id))
            .OrderBy(audit => audit.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(attempts.Length, auditEvents.Count);
        Assert.All(auditEvents, audit =>
        {
            Assert.Equal(tenantB, audit.TenantId);
            Assert.Equal(tenantB, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal(TestJwtTokenFactory.Subject, audit.ActorSubject);
            Assert.Equal((int)HttpStatusCode.NotFound, audit.StatusCode);
            Assert.Equal("Rejected", audit.Outcome);
        });
    }

    private async Task<(Guid HospitalId, Guid DepartmentId)> SeedHospitalAndDepartmentAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Staff test tenant",
            Slug = $"staff-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital { Name = "Staff Test Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();

        var department = new Department { HospitalId = hospital.Id, Name = "Medicine", Code = "MED" };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return (hospital.Id, department.Id);
    }

    private HttpClient CreateClient(Guid tenantId, string? subject = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken(subject: subject));
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
    }
}
