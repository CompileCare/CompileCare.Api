using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Patients;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class PatientDuplicateDetectionApiTests
{
    private readonly FacilityApiFactory _factory;

    public PatientDuplicateDetectionApiTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task DuplicateCheck_ReturnsHospitalLocalCandidates_AndNeverLinksRecords()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar);
        var hospitalId = await SeedHospitalAsync(tenantId, "Duplicate Check Hospital");
        var otherHospitalId = await SeedHospitalAsync(tenantId, "Other Duplicate Check Hospital");
        using var client = CreateClient(tenantId);

        var localPatient = await RegisterPatientAsync(
            client, hospitalId, "Asha", "R.", "Patil", new DateOnly(1990, 4, 12));
        _ = await RegisterPatientAsync(
            client, otherHospitalId, "Asha", "R.", "Patil", new DateOnly(1990, 4, 12));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/duplicate-candidates",
            new PatientDuplicateCheckRequest("asha", "R.", "Patil", new DateOnly(1990, 4, 12)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PatientDuplicateCheckDto>();
        Assert.NotNull(result);
        Assert.True(result!.Evaluated);
        Assert.False(result.HasMoreCandidates);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(localPatient.Id, candidate.PatientId);
        Assert.Contains("Exact given name (case-insensitive)", candidate.MatchSignals);
        Assert.Contains("Exact date of birth", candidate.MatchSignals);
        Assert.Contains("Exact middle name", candidate.MatchSignals);
        Assert.Contains("Exact family name", candidate.MatchSignals);

        // Candidate detection is advisory only. Registration remains a separate
        // explicit action and does not silently associate the new person.
        var newRegistration = await RegisterPatientAsync(
            client, hospitalId, "Asha", "R.", "Patil", new DateOnly(1990, 4, 12));
        Assert.NotEqual(localPatient.Id, newRegistration.Id);
    }

    [Fact]
    public async Task DuplicateCheck_WithoutDateOfBirthDoesNotRunNameOnlySearch()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar);
        var hospitalId = await SeedHospitalAsync(tenantId, "Unknown DOB Hospital");
        using var client = CreateClient(tenantId);

        _ = await RegisterPatientAsync(
            client, hospitalId, "Asha", null, "Patil", new DateOnly(1990, 4, 12));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/duplicate-candidates",
            new PatientDuplicateCheckRequest("Asha", null, "Patil", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PatientDuplicateCheckDto>();
        Assert.NotNull(result);
        Assert.False(result!.Evaluated);
        Assert.Empty(result.Candidates);
        Assert.False(result.HasMoreCandidates);
        Assert.Contains("date of birth is unavailable", result.EvaluationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateCheck_RequiresDedicatedPermission()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.FacilityManager);
        var hospitalId = await SeedHospitalAsync(tenantId, "Permission Hospital");
        using var client = CreateClient(tenantId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/duplicate-candidates",
            new PatientDuplicateCheckRequest("Asha", null, "Patil", new DateOnly(1990, 4, 12)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HospitalScopedRegistrar_CannotCheckDuplicatesInAnotherHospital()
    {
        var tenantId = Guid.NewGuid();
        var (authorizedHospitalId, otherHospitalId) = await SeedTenantAndTwoHospitalsAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, authorizedHospitalId)]);
        using var client = CreateClient(tenantId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{otherHospitalId:D}/patients/duplicate-candidates",
            new PatientDuplicateCheckRequest("Asha", null, "Patil", new DateOnly(1990, 4, 12)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<PatientDto> RegisterPatientAsync(
        HttpClient client,
        Guid hospitalId,
        string givenName,
        string? middleName,
        string? familyName,
        DateOnly? dateOfBirth)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients",
            new PatientRegistrationInput(givenName, middleName, familyName, dateOfBirth));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var patient = await response.Content.ReadFromJsonAsync<PatientDto>();
        Assert.NotNull(patient);
        return patient!;
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

    private async Task<(Guid AuthorizedHospitalId, Guid OtherHospitalId)> SeedTenantAndTwoHospitalsAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Duplicate check scope tenant",
            Slug = $"duplicate-scope-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var authorized = new Hospital { Name = "Authorized Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
        var other = new Hospital { Name = "Other Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
        db.Hospitals.AddRange(authorized, other);
        await db.SaveChangesAsync();
        return (authorized.Id, other.Id);
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
