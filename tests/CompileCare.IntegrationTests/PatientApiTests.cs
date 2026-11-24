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
    public async Task PatientRegistrar_DoesNotReceiveFacilityAdministrationPermissions()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar);
        using var client = CreateClient(tenantId);

        var response = await client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
