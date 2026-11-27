using System.Net;
using System.Net.Http.Headers;
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
public sealed class PatientIdentifierOrganizationScopeApiTests(FacilityApiFactory factory)
{
    [Fact]
    public async Task HospitalAndDepartmentScopes_CannotCrossHospitalOrManageHospitalWideMrnPolicy()
    {
        factory.ResetDatabase();

        var tenantId = Guid.NewGuid();
        var (hospitalA, departmentA, hospitalB, departmentB) = await SeedFacilitiesAsync(tenantId);

        const string tenantAdminSubject = "mrn-scope-tenant-admin";
        const string registrarSubject = "mrn-scope-registrar";
        const string hospitalAdminSubject = "mrn-scope-hospital-admin";
        const string departmentAdminSubject = "mrn-scope-department-admin";

        await SeedMembershipAsync(tenantId, tenantAdminSubject, TenantRoles.HospitalAdmin,
            [new OrganizationScope(OrganizationScopeTypes.Tenant)]);
        await SeedMembershipAsync(tenantId, registrarSubject, TenantRoles.PatientRegistrar,
            [new OrganizationScope(OrganizationScopeTypes.Tenant)]);
        await SeedMembershipAsync(tenantId, hospitalAdminSubject, TenantRoles.HospitalAdmin,
            [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalA)]);
        await SeedMembershipAsync(tenantId, departmentAdminSubject, TenantRoles.HospitalAdmin,
            [new OrganizationScope(OrganizationScopeTypes.Department, hospitalA, departmentA)]);

        using var tenantAdmin = CreateClient(tenantId, tenantAdminSubject);
        using var registrar = CreateClient(tenantId, registrarSubject);
        using var hospitalAdmin = CreateClient(tenantId, hospitalAdminSubject);
        using var departmentAdmin = CreateClient(tenantId, departmentAdminSubject);

        var policyA = await tenantAdmin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}/patient-identifier-policy",
            new PatientIdentifierPolicyInput("HOSA", 6));
        var policyB = await tenantAdmin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalB:D}/patient-identifier-policy",
            new PatientIdentifierPolicyInput("HOSB", 6));
        Assert.Equal(HttpStatusCode.Created, policyA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, policyB.StatusCode);

        var patientA = await RegisterPatientAsync(registrar, hospitalA, "Asha");
        var patientB = await RegisterPatientAsync(registrar, hospitalB, "Rohan");

        var identifierAResponse = await registrar.PostAsync(
            $"/api/v1/hospitals/{hospitalA:D}/patients/{patientA.Id:D}/identifiers/mrn", null);
        var identifierBResponse = await registrar.PostAsync(
            $"/api/v1/hospitals/{hospitalB:D}/patients/{patientB.Id:D}/identifiers/mrn", null);
        Assert.Equal(HttpStatusCode.OK, identifierAResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, identifierBResponse.StatusCode);
        var identifierA = await identifierAResponse.Content.ReadFromJsonAsync<PatientIdentifierDto>();
        var identifierB = await identifierBResponse.Content.ReadFromJsonAsync<PatientIdentifierDto>();
        Assert.NotNull(identifierA);
        Assert.NotNull(identifierB);

        // Hospital-scoped administrators retain access to their own hospital.
        var ownPolicy = await hospitalAdmin.GetAsync(
            $"/api/v1/hospitals/{hospitalA:D}/patient-identifier-policy");
        var ownIdentifiers = await hospitalAdmin.GetAsync(
            $"/api/v1/hospitals/{hospitalA:D}/patients/{patientA.Id:D}/identifiers");
        var ownLookup = await hospitalAdmin.GetAsync(
            $"/api/v1/hospitals/{hospitalA:D}/patients/by-mrn/{identifierA!.Value}");
        var ownAssignment = await hospitalAdmin.PostAsync(
            $"/api/v1/hospitals/{hospitalA:D}/patients/{patientA.Id:D}/identifiers/mrn", null);
        Assert.Equal(HttpStatusCode.OK, ownPolicy.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownIdentifiers.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownLookup.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownAssignment.StatusCode);

        // Capture the audit watermark after all successful setup and allowed requests.
        using var auditBaselineScope = factory.Services.CreateScope();
        auditBaselineScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditBaselineDb = auditBaselineScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var scopedSubjects = new[] { hospitalAdminSubject, departmentAdminSubject };
        var existingAuditIds = await auditBaselineDb.AuditEvents.AsNoTracking()
            .Where(audit => scopedSubjects.Contains(audit.ActorSubject))
            .Select(audit => audit.Id)
            .ToListAsync();

        var denied = new[]
        {
            await hospitalAdmin.GetAsync(
                $"/api/v1/hospitals/{hospitalB:D}/patient-identifier-policy"),
            await hospitalAdmin.PostAsJsonAsync(
                $"/api/v1/hospitals/{hospitalB:D}/patient-identifier-policy",
                new PatientIdentifierPolicyInput("OUTSIDE", 5)),
            await hospitalAdmin.GetAsync(
                $"/api/v1/hospitals/{hospitalB:D}/patients/{patientB.Id:D}/identifiers"),
            await hospitalAdmin.GetAsync(
                $"/api/v1/hospitals/{hospitalB:D}/patients/by-mrn/{identifierB!.Value}"),
            await hospitalAdmin.PostAsync(
                $"/api/v1/hospitals/{hospitalB:D}/patients/{patientB.Id:D}/identifiers/mrn", null),
            await departmentAdmin.GetAsync(
                $"/api/v1/hospitals/{hospitalA:D}/patient-identifier-policy"),
            await departmentAdmin.PostAsJsonAsync(
                $"/api/v1/hospitals/{hospitalA:D}/patient-identifier-policy",
                new PatientIdentifierPolicyInput("DEPT", 5)),
            await departmentAdmin.GetAsync(
                $"/api/v1/hospitals/{hospitalA:D}/patients/{patientA.Id:D}/identifiers"),
            await departmentAdmin.GetAsync(
                $"/api/v1/hospitals/{hospitalA:D}/patients/by-mrn/{identifierA.Value}"),
            await departmentAdmin.PostAsync(
                $"/api/v1/hospitals/{hospitalA:D}/patients/{patientA.Id:D}/identifiers/mrn", null)
        };

        Assert.All(denied, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var deniedBodies = await Task.WhenAll(denied.Select(response => response.Content.ReadAsStringAsync()));
        Assert.All(deniedBodies, body =>
        {
            Assert.DoesNotContain(identifierA.Value, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(identifierB.Value, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Asha", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Rohan", body, StringComparison.Ordinal);
        });

        // A tenant-wide administrator confirms neither rejected policy write nor
        // rejected assignment changed either hospital's MRN state.
        var retainedPolicyA = await tenantAdmin.GetFromJsonAsync<PatientIdentifierPolicyDto>(
            $"/api/v1/hospitals/{hospitalA:D}/patient-identifier-policy");
        var retainedPolicyB = await tenantAdmin.GetFromJsonAsync<PatientIdentifierPolicyDto>(
            $"/api/v1/hospitals/{hospitalB:D}/patient-identifier-policy");
        Assert.NotNull(retainedPolicyA);
        Assert.NotNull(retainedPolicyB);
        Assert.Equal("HOSA", retainedPolicyA!.Prefix);
        Assert.Equal("HOSB", retainedPolicyB!.Prefix);

        var retainedA = await tenantAdmin.GetFromJsonAsync<List<PatientIdentifierDto>>(
            $"/api/v1/hospitals/{hospitalA:D}/patients/{patientA.Id:D}/identifiers");
        var retainedB = await tenantAdmin.GetFromJsonAsync<List<PatientIdentifierDto>>(
            $"/api/v1/hospitals/{hospitalB:D}/patients/{patientB.Id:D}/identifiers");
        Assert.Equal(identifierA.Id, Assert.Single(retainedA!).Id);
        Assert.Equal(identifierB.Id, Assert.Single(retainedB!).Id);

        using var auditScope = factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var rejectedAudits = await auditDb.AuditEvents.AsNoTracking()
            .Where(audit =>
                !existingAuditIds.Contains(audit.Id) &&
                audit.TenantId == tenantId &&
                audit.Outcome == "Rejected" &&
                scopedSubjects.Contains(audit.ActorSubject))
            .ToListAsync();

        Assert.Equal(denied.Length, rejectedAudits.Count);
        Assert.Equal(5, rejectedAudits.Count(audit => audit.ActorSubject == hospitalAdminSubject));
        Assert.Equal(5, rejectedAudits.Count(audit => audit.ActorSubject == departmentAdminSubject));
        Assert.All(rejectedAudits, audit =>
        {
            Assert.Equal(tenantId, audit.TenantId);
            Assert.Equal(tenantId, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal((int)HttpStatusCode.NotFound, audit.StatusCode);
            Assert.Equal("Rejected", audit.Outcome);
        });
    }

    private async Task<PatientDto> RegisterPatientAsync(HttpClient client, Guid hospitalId, string givenName)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients",
            new PatientRegistrationInput(givenName, null, "Scope", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var patient = await response.Content.ReadFromJsonAsync<PatientDto>();
        Assert.NotNull(patient);
        return patient!;
    }

    private async Task<(Guid HospitalA, Guid DepartmentA, Guid HospitalB, Guid DepartmentB)> SeedFacilitiesAsync(Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "MRN organization scope tenant",
            Slug = $"mrn-org-scope-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospitalA = new Hospital { Name = "MRN Scope Hospital A", Code = Guid.NewGuid().ToString("N")[..8] };
        var hospitalB = new Hospital { Name = "MRN Scope Hospital B", Code = Guid.NewGuid().ToString("N")[..8] };
        db.Hospitals.AddRange(hospitalA, hospitalB);
        await db.SaveChangesAsync();

        var departmentA = new Department { HospitalId = hospitalA.Id, Name = "Medicine", Code = "MED" };
        var departmentB = new Department { HospitalId = hospitalB.Id, Name = "Cardiology", Code = "CARD" };
        db.Departments.AddRange(departmentA, departmentB);
        await db.SaveChangesAsync();

        return (hospitalA.Id, departmentA.Id, hospitalB.Id, departmentB.Id);
    }

    private async Task SeedMembershipAsync(
        Guid tenantId,
        string subject,
        string role,
        IReadOnlyList<OrganizationScope> scopes)
    {
        await factory.SeedTenantMembershipAsync(tenantId, role, scopes: scopes, subject: subject);
    }

    private HttpClient CreateClient(Guid tenantId, string subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken(subject));
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
    }
}
