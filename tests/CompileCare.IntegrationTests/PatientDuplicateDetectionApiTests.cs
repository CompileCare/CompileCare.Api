using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Patients;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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
        Assert.Contains("Normalized given name match", candidate.MatchSignals);
        Assert.Contains("Exact date of birth", candidate.MatchSignals);
        Assert.Contains("Normalized middle name match", candidate.MatchSignals);
        Assert.Contains("Normalized family name match", candidate.MatchSignals);

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

    [Fact]
    public async Task DuplicateCheck_BoundsCandidateResultToTwenty()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar);
        var hospitalId = await SeedHospitalAsync(tenantId, "Candidate Limit Hospital");
        using var client = CreateClient(tenantId);

        for (var i = 0; i < 21; i++)
        {
            _ = await RegisterPatientAsync(
                client, hospitalId, "Asha", null, $"Patil{i:D2}", new DateOnly(1990, 4, 12));
        }

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/duplicate-candidates",
            new PatientDuplicateCheckRequest("Asha", null, null, new DateOnly(1990, 4, 12)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PatientDuplicateCheckDto>();
        Assert.NotNull(result);
        Assert.True(result!.Evaluated);
        Assert.Equal(20, result.Candidates.Count);
        Assert.True(result.HasMoreCandidates);
    }

    [Fact]
    public async Task DepartmentScopedRegistrar_CannotRunHospitalWideDuplicateCheck()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId) = await SeedHospitalAndDepartmentAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, departmentId)]);
        using var client = CreateClient(tenantId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/duplicate-candidates",
            new PatientDuplicateCheckRequest("Asha", null, null, new DateOnly(1990, 4, 12)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateCheck_MatchesUnicodeCompatibilityAndCollapsedWhitespace()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar);
        var hospitalId = await SeedHospitalAsync(tenantId, "Normalized Name Hospital");
        using var client = CreateClient(tenantId);

        var patient = await RegisterPatientAsync(
            client, hospitalId, "Ａsha   Patil", null, null, new DateOnly(1990, 4, 12));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/duplicate-candidates",
            new PatientDuplicateCheckRequest("asha patil", null, null, new DateOnly(1990, 4, 12)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PatientDuplicateCheckDto>();
        Assert.NotNull(result);
        var candidate = Assert.Single(result!.Candidates);
        Assert.Equal(patient.Id, candidate.PatientId);
        Assert.Contains("Normalized given name match", candidate.MatchSignals);
    }

    [Fact]
    public async Task DuplicateCheck_UsesMiddleAndFamilyAsAlternateSignalOnlyWhenBothMatch()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar);
        var hospitalId = await SeedHospitalAsync(tenantId, "Multi Signal Hospital");
        using var client = CreateClient(tenantId);

        var patient = await RegisterPatientAsync(
            client, hospitalId, "Asha", "R", "Patil", new DateOnly(1990, 4, 12));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/duplicate-candidates",
            new PatientDuplicateCheckRequest("Anita", " R ", "Patil", new DateOnly(1990, 4, 12)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PatientDuplicateCheckDto>();
        Assert.NotNull(result);
        var candidate = Assert.Single(result!.Candidates);
        Assert.Equal(patient.Id, candidate.PatientId);
        Assert.Contains("Normalized middle name match", candidate.MatchSignals);
        Assert.Contains("Normalized family name match", candidate.MatchSignals);
        Assert.Contains("Middle and family names matched; given name differed", candidate.MatchSignals);
        Assert.DoesNotContain("Normalized given name match", candidate.MatchSignals);
    }

    [Fact]
    public async Task DuplicateCheck_DoesNotUseFamilyNameAloneOrRelaxExactDateOfBirth()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar);
        var hospitalId = await SeedHospitalAsync(tenantId, "Strict Match Hospital");
        using var client = CreateClient(tenantId);

        _ = await RegisterPatientAsync(
            client, hospitalId, "Asha", "R", "Patil", new DateOnly(1990, 4, 12));

        var familyOnly = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/duplicate-candidates",
            new PatientDuplicateCheckRequest("Anita", null, "Patil", new DateOnly(1990, 4, 12)));
        var differentDob = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/duplicate-candidates",
            new PatientDuplicateCheckRequest("Asha", "R", "Patil", new DateOnly(1990, 4, 13)));

        Assert.Equal(HttpStatusCode.OK, familyOnly.StatusCode);
        Assert.Equal(HttpStatusCode.OK, differentDob.StatusCode);
        var familyOnlyResult = await familyOnly.Content.ReadFromJsonAsync<PatientDuplicateCheckDto>();
        var differentDobResult = await differentDob.Content.ReadFromJsonAsync<PatientDuplicateCheckDto>();
        Assert.NotNull(familyOnlyResult);
        Assert.NotNull(differentDobResult);
        Assert.Empty(familyOnlyResult!.Candidates);
        Assert.Empty(differentDobResult!.Candidates);
    }

    [Fact]
    public async Task DuplicateCheck_CannotDiscoverCandidatesOwnedByAnotherTenant_AndDenialIsAudited()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string tenantASubject = "duplicate-tenant-a-registrar";
        const string tenantBSubject = "duplicate-tenant-b-registrar";

        await _factory.SeedTenantMembershipAsync(
            tenantA, TenantRoles.PatientRegistrar, subject: tenantASubject);
        await _factory.SeedTenantMembershipAsync(
            tenantB, TenantRoles.PatientRegistrar, subject: tenantBSubject);

        var hospitalA = await SeedHospitalAsync(tenantA, "Tenant A Duplicate Hospital");
        var hospitalB = await SeedHospitalAsync(tenantB, "Tenant B Duplicate Hospital");

        using var registrarA = CreateClient(tenantA, tenantASubject);
        using var registrarB = CreateClient(tenantB, tenantBSubject);

        _ = await RegisterPatientAsync(
            registrarA, hospitalA, "Asha", "R.", "Patil", new DateOnly(1990, 4, 12));

        // Capture the tenant-B audit watermark after fixture setup and before the requests under test.
        using var auditBaselineScope = _factory.Services.CreateScope();
        auditBaselineScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantB);
        var auditBaselineDb = auditBaselineScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var existingAuditIds = await auditBaselineDb.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorSubject == tenantBSubject)
            .Select(audit => audit.Id)
            .ToListAsync();

        // A same-name/DOB patient exists only in tenant A. Searching tenant B must not disclose it.
        var tenantBSearch = await registrarB.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalB:D}/patients/duplicate-candidates",
            new PatientDuplicateCheckRequest("Asha", "R.", "Patil", new DateOnly(1990, 4, 12)));

        Assert.Equal(HttpStatusCode.OK, tenantBSearch.StatusCode);
        var tenantBResult = await tenantBSearch.Content.ReadFromJsonAsync<PatientDuplicateCheckDto>();
        Assert.NotNull(tenantBResult);
        Assert.True(tenantBResult!.Evaluated);
        Assert.Empty(tenantBResult.Candidates);
        Assert.False(tenantBResult.HasMoreCandidates);
        var tenantBBody = await tenantBSearch.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Asha", tenantBBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Patil", tenantBBody, StringComparison.Ordinal);

        // A tenant-B member cannot run the same operation against tenant A's hospital at all.
        var crossTenantAttempt = await registrarB.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}/patients/duplicate-candidates",
            new PatientDuplicateCheckRequest("Asha", "R.", "Patil", new DateOnly(1990, 4, 12)));

        Assert.Equal(HttpStatusCode.NotFound, crossTenantAttempt.StatusCode);
        var deniedBody = await crossTenantAttempt.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Asha", deniedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Patil", deniedBody, StringComparison.Ordinal);

        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantB);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var auditEvents = await auditDb.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorSubject == tenantBSubject &&
                            !existingAuditIds.Contains(audit.Id))
            .OrderBy(audit => audit.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(2, auditEvents.Count);
        Assert.Equal(
            new[] { (int)HttpStatusCode.OK, (int)HttpStatusCode.NotFound },
            auditEvents.Select(audit => audit.StatusCode).ToArray());
        Assert.Equal(
            new[] { "Succeeded", "Rejected" },
            auditEvents.Select(audit => audit.Outcome).ToArray());
        Assert.All(auditEvents, audit =>
        {
            Assert.Equal(tenantB, audit.TenantId);
            Assert.Equal(tenantB, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal(tenantBSubject, audit.ActorSubject);
            Assert.Equal("FindPatientDuplicateCandidates", audit.Action);
        });
        Assert.Equal(hospitalB, auditEvents[0].ResourceId);
        Assert.Equal(hospitalA, auditEvents[1].ResourceId);
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

    private async Task<(Guid HospitalId, Guid DepartmentId)> SeedHospitalAndDepartmentAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Duplicate department scope tenant",
            Slug = $"duplicate-department-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital
        {
            Name = "Department-scoped Duplicate Hospital",
            Code = Guid.NewGuid().ToString("N")[..8]
        };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();

        var department = new Department { HospitalId = hospital.Id, Name = "Cardiology", Code = "CARD" };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return (hospital.Id, department.Id);
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

    private HttpClient CreateClient(Guid tenantId, string? subject = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken(subject: subject));
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
    }
}
