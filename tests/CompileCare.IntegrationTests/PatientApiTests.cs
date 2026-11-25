using System.Net;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Patients;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class PatientApiTests
{
    private readonly FacilityApiFactory _factory;

    public PatientApiTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task PatientRegistrar_CanRegisterAndSearchOnlyPatientsRegisteredAtSelectedHospital()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar);
        var hospitalId = await SeedHospitalAsync(tenantId, "Patient Hospital");
        var otherHospitalId = await SeedHospitalAsync(tenantId, "Other Hospital");

        using var client = CreateClient(tenantId);
        var register = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients",
            new PatientRegistrationInput("Asha", "R.", "Patil", new DateOnly(1990, 4, 12)));

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var created = await register.Content.ReadFromJsonAsync<PatientDto>();
        Assert.NotNull(created);
        Assert.Equal(hospitalId, created!.HospitalId);
        Assert.Equal("Asha", created.GivenName);

        var search = await client.GetFromJsonAsync<PatientPage>(
            $"/api/v1/hospitals/{hospitalId:D}/patients?query=Asha&page=1&pageSize=10");
        Assert.NotNull(search);
        Assert.Single(search!.Items);
        Assert.Equal(created.Id, search.Items[0].Id);
        var details = await client.GetFromJsonAsync<PatientDto>(
            $"/api/v1/hospitals/{hospitalId:D}/patients/{created.Id:D}");
        Assert.NotNull(details);
        Assert.Equal(created.Id, details!.Id);

        var otherHospitalSearch = await client.GetFromJsonAsync<PatientPage>(
            $"/api/v1/hospitals/{otherHospitalId:D}/patients?page=1&pageSize=10");
        Assert.NotNull(otherHospitalSearch);
        Assert.Empty(otherHospitalSearch!.Items);
    }

    [Fact]
    public async Task FacilityRoles_DoNotImplicitlyGrantPatientAccess()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.FacilityManager);
        var hospitalId = await SeedHospitalAsync(tenantId, "Restricted Hospital");
        using var client = CreateClient(tenantId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients",
            new PatientRegistrationInput("No", null, "Access", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PatientRegistrar_CanReadButCannotAdministerFacilities()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar);
        var hospitalId = await SeedHospitalAsync(tenantId, "Reception Hospital");
        using var client = CreateClient(tenantId);

        // Reception needs read-only hospital discovery for its hospital selector.
        var read = await client.GetAsync("/api/v1/hospitals?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var create = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Unauthorized Hospital", code = Guid.NewGuid().ToString("N")[..8] });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var update = await client.PutAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}",
            new { name = "Changed Hospital", code = "CHANGED" });
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        var delete = await client.DeleteAsync($"/api/v1/hospitals/{hospitalId:D}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Registration_RejectsMissingNameAndFutureDateOfBirth()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar);
        var hospitalId = await SeedHospitalAsync(tenantId, "Validation Hospital");
        using var client = CreateClient(tenantId);

        var missingName = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients",
            new PatientRegistrationInput(" ", null, "Patil", null));
        var futureDate = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients",
            new PatientRegistrationInput("Asha", null, "Patil", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))));

        Assert.Equal(HttpStatusCode.BadRequest, missingName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, futureDate.StatusCode);
    }


    [Fact]
    public async Task DepartmentScopedPatientRegistrar_CannotReadHospitalWidePatientRecords()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId) = await SeedHospitalAndDepartmentAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, departmentId)]);

        using (var scope = _factory.Services.CreateScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<ITenantMembershipReader>();
            var membership = await reader.FindActiveMembershipAsync(
                tenantId, TestJwtTokenFactory.Issuer, TestJwtTokenFactory.Subject, CancellationToken.None);
            Assert.NotNull(membership);
            Assert.Equal(OrganizationScopeTypes.Department, Assert.Single(membership!.Scopes).ScopeType);
        }

        var scopeContext = new OrganizationScopeContext();
        scopeContext.SetMembership(Guid.NewGuid(), TenantRoles.PatientRegistrar,
            [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, departmentId)]);
        Assert.False(scopeContext.IsTenantWide);
        Assert.Empty(scopeContext.HospitalIds);

        using var client = CreateClient(tenantId);

        var response = await client.GetAsync($"/api/v1/hospitals/{hospitalId:D}/patients");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }


    [Fact]
    public async Task HospitalScopedPatientRegistrar_CannotReadAnotherHospitalsRegistrations()
    {
        var tenantId = Guid.NewGuid();
        var (authorizedHospitalId, otherHospitalId) = await SeedTenantAndTwoHospitalsAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, authorizedHospitalId)]);
        using var client = CreateClient(tenantId);

        var response = await client.GetAsync($"/api/v1/hospitals/{otherHospitalId:D}/patients");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Guid AuthorizedHospitalId, Guid OtherHospitalId)> SeedTenantAndTwoHospitalsAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Hospital-scoped test tenant",
            Slug = $"hospital-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var authorized = new Hospital { Name = "Authorized Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
        var other = new Hospital { Name = "Other Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
        db.Hospitals.AddRange(authorized, other);
        await db.SaveChangesAsync();
        return (authorized.Id, other.Id);
    }

    private async Task<(Guid HospitalId, Guid DepartmentId)> SeedHospitalAndDepartmentAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Department-scoped test tenant",
            Slug = $"department-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital { Name = "Department Scope Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();

        var department = new Department { HospitalId = hospital.Id, Name = "Cardiology", Code = "CARD" };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return (hospital.Id, department.Id);
    }

    private async Task<Guid> SeedHospitalAsync(Guid tenantId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var hospital = new Hospital { Name = name, Code = Guid.NewGuid().ToString("N")[..8] };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();
        return hospital.Id;
    }

    private HttpClient CreateClient(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", TestJwtTokenFactory.CreateToken());
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
    }
}
