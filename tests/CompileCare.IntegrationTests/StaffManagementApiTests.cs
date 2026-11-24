using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Staff;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
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

    private HttpClient CreateClient(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken());
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
    }
}
