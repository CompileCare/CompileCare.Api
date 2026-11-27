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
public sealed class PatientIdentifierAuthorizationApiTests(FacilityApiFactory factory)
{
    [Fact]
    public async Task PersistedRoles_EnforceIdentifierAndPolicyPermissions_AndAuditDenialsWithoutMutation()
    {
        factory.ResetDatabase();

        var tenantId = Guid.NewGuid();
        var hospitalId = await SeedHospitalAsync(tenantId);

        const string adminSubject = "mrn-auth-admin";
        const string registrarSubject = "mrn-auth-registrar";
        const string managerSubject = "mrn-auth-facility-manager";
        const string viewerSubject = "mrn-auth-facility-viewer";
        const string clinicalSubject = "mrn-auth-clinical-user";

        await SeedMembershipAsync(tenantId, hospitalId, adminSubject, TenantRoles.HospitalAdmin);
        await SeedMembershipAsync(tenantId, hospitalId, registrarSubject, TenantRoles.PatientRegistrar);
        await SeedMembershipAsync(tenantId, hospitalId, managerSubject, TenantRoles.FacilityManager);
        await SeedMembershipAsync(tenantId, hospitalId, viewerSubject, TenantRoles.FacilityViewer);
        await SeedMembershipAsync(tenantId, hospitalId, clinicalSubject, TenantRoles.ClinicalUser);

        using var admin = CreateClient(tenantId, adminSubject);
        using var registrar = CreateClient(tenantId, registrarSubject);
        using var manager = CreateClient(tenantId, managerSubject);
        using var viewer = CreateClient(tenantId, viewerSubject);
        using var clinical = CreateClient(tenantId, clinicalSubject);

        var policyCreated = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patient-identifier-policy",
            new PatientIdentifierPolicyInput("AUTH", 6));
        Assert.Equal(HttpStatusCode.Created, policyCreated.StatusCode);

        // Reception users load the hospital selector before patient workflows.
        var hospitals = await registrar.GetAsync("/api/v1/hospitals?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, hospitals.StatusCode);

        var patientResponse = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients",
            new PatientRegistrationInput("Asha", null, "Patil", null));
        Assert.Equal(HttpStatusCode.Created, patientResponse.StatusCode);
        var patient = await patientResponse.Content.ReadFromJsonAsync<PatientDto>();
        Assert.NotNull(patient);

        var assignment = await registrar.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/{patient!.Id:D}/identifiers/mrn",
            content: null);
        Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);
        var identifier = await assignment.Content.ReadFromJsonAsync<PatientIdentifierDto>();
        Assert.NotNull(identifier);

        // Capture the audit watermark after successful setup so only this test's
        // denied operations are counted below.
        using var auditBaselineScope = factory.Services.CreateScope();
        auditBaselineScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditBaselineDb = auditBaselineScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var existingAuditIds = await auditBaselineDb.AuditEvents.AsNoTracking()
            .Where(audit => new[] { managerSubject, viewerSubject, clinicalSubject, registrarSubject }
                .Contains(audit.ActorSubject))
            .Select(audit => audit.Id)
            .ToListAsync();

        var deniedResponses = new List<HttpResponseMessage>();
        foreach (var client in new[] { manager, viewer, clinical })
        {
            deniedResponses.Add(await client.GetAsync(
                $"/api/v1/hospitals/{hospitalId:D}/patient-identifier-policy"));
            deniedResponses.Add(await client.PostAsJsonAsync(
                $"/api/v1/hospitals/{hospitalId:D}/patient-identifier-policy",
                new PatientIdentifierPolicyInput("DENIED", 5)));
            deniedResponses.Add(await client.GetAsync(
                $"/api/v1/hospitals/{hospitalId:D}/patients/{patient.Id:D}/identifiers"));
            deniedResponses.Add(await client.GetAsync(
                $"/api/v1/hospitals/{hospitalId:D}/patients/by-mrn/{identifier!.Value}"));
            deniedResponses.Add(await client.PostAsync(
                $"/api/v1/hospitals/{hospitalId:D}/patients/{patient.Id:D}/identifiers/mrn",
                content: null));
        }

        // PatientRegistrar may read and assign identifiers, but cannot inspect or
        // change the hospital-wide identifier policy.
        deniedResponses.Add(await registrar.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patient-identifier-policy"));
        deniedResponses.Add(await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patient-identifier-policy",
            new PatientIdentifierPolicyInput("REGDENY", 5)));

        Assert.Equal(17, deniedResponses.Count);
        Assert.All(deniedResponses, response =>
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));

        // The registrar's allowed identifier capabilities remain functional.
        var registrarIdentifiers = await registrar.GetFromJsonAsync<List<PatientIdentifierDto>>(
            $"/api/v1/hospitals/{hospitalId:D}/patients/{patient.Id:D}/identifiers");
        Assert.NotNull(registrarIdentifiers);
        var retainedIdentifier = Assert.Single(registrarIdentifiers!);
        Assert.Equal(identifier!.Id, retainedIdentifier.Id);
        Assert.Equal(identifier.Value, retainedIdentifier.Value);

        var registrarLookup = await registrar.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/by-mrn/{identifier.Value}");
        Assert.Equal(HttpStatusCode.OK, registrarLookup.StatusCode);

        var registrarIdempotentAssignment = await registrar.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/{patient.Id:D}/identifiers/mrn",
            content: null);
        Assert.Equal(HttpStatusCode.OK, registrarIdempotentAssignment.StatusCode);
        var sameIdentifier = await registrarIdempotentAssignment.Content.ReadFromJsonAsync<PatientIdentifierDto>();
        Assert.NotNull(sameIdentifier);
        Assert.Equal(identifier.Id, sameIdentifier!.Id);
        Assert.Equal(identifier.Value, sameIdentifier.Value);

        // Verify denied policy writes did not replace the configured policy and
        // denied assignments did not create or alter identifiers.
        var retainedPolicy = await admin.GetFromJsonAsync<PatientIdentifierPolicyDto>(
            $"/api/v1/hospitals/{hospitalId:D}/patient-identifier-policy");
        Assert.NotNull(retainedPolicy);
        Assert.Equal("AUTH", retainedPolicy!.Prefix);
        Assert.Equal(6, retainedPolicy.NumberLength);

        var adminIdentifiers = await admin.GetFromJsonAsync<List<PatientIdentifierDto>>(
            $"/api/v1/hospitals/{hospitalId:D}/patients/{patient.Id:D}/identifiers");
        Assert.NotNull(adminIdentifiers);
        var adminIdentifier = Assert.Single(adminIdentifiers!);
        Assert.Equal(identifier.Id, adminIdentifier.Id);
        Assert.Equal(identifier.Value, adminIdentifier.Value);

        using var auditScope = factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var deniedAudits = await auditDb.AuditEvents.AsNoTracking()
            .Where(audit =>
                !existingAuditIds.Contains(audit.Id) &&
                audit.TenantId == tenantId &&
                audit.Outcome == "Denied" &&
                new[] { managerSubject, viewerSubject, clinicalSubject, registrarSubject }
                    .Contains(audit.ActorSubject))
            .ToListAsync();

        Assert.Equal(deniedResponses.Count, deniedAudits.Count);
        Assert.Equal(15, deniedAudits.Count(audit => audit.ActorSubject != registrarSubject));
        Assert.Equal(2, deniedAudits.Count(audit => audit.ActorSubject == registrarSubject));
        Assert.All(deniedAudits, audit =>
        {
            Assert.Equal(tenantId, audit.TenantId);
            Assert.Equal(tenantId, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal((int)HttpStatusCode.Forbidden, audit.StatusCode);
            Assert.Equal("Denied", audit.Outcome);
        });
    }

    private async Task<Guid> SeedHospitalAsync(Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "MRN authorization test tenant",
            Slug = $"mrn-auth-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital
        {
            Name = "MRN Authorization Test Hospital",
            Code = Guid.NewGuid().ToString("N")[..8]
        };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();
        return hospital.Id;
    }

    private async Task SeedMembershipAsync(
        Guid tenantId,
        Guid hospitalId,
        string subject,
        string role)
    {
        await factory.SeedTenantMembershipAsync(
            tenantId,
            role,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: subject);
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
