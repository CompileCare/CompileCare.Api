using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Appointments;
using CompileCare.Application.Clinical;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class OpdEncounterAuthorizationTests
{
    private readonly FacilityApiFactory _factory;

    public OpdEncounterAuthorizationTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Theory]
    [InlineData(TenantRoles.FacilityManager)]
    [InlineData(TenantRoles.FacilityViewer)]
    [InlineData(TenantRoles.PatientRegistrar)]
    public async Task RolesWithoutClinicalGrants_CannotReadOrMutateOpdEncounters(string role)
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, patientId) = await SeedPatientAsync(tenantId);
        const string registrarSubject = "opd-auth-frontdesk";
        const string clinicianSubject = "opd-auth-clinician";
        const string restrictedSubject = "opd-auth-restricted";

        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: registrarSubject);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.ClinicalUser,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: clinicianSubject);
        await _factory.SeedTenantMembershipAsync(
            tenantId, role,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: restrictedSubject);

        using var registrar = CreateClient(tenantId, registrarSubject);
        using var clinician = CreateClient(tenantId, clinicianSubject);
        using var restricted = CreateClient(tenantId, restrictedSubject);

        var walkIn = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/walk-ins",
            new WalkInCreateInput(departmentId, patientId, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, walkIn.StatusCode);
        var queueEntry = await walkIn.Content.ReadFromJsonAsync<QueueEntryDto>();
        Assert.NotNull(queueEntry);

        var call = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{queueEntry!.Id:D}/call",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        var start = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{queueEntry.Id:D}/start",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var create = await clinician.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/queue/{queueEntry.Id:D}/encounter", null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var encounter = await create.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(encounter);

        const string privateClinicalText = "RestrictedRoleMustNotSee-Clinical-Note";
        var note = new OpdEncounterNoteInput(
            privateClinicalText,
            "Confidential patient history",
            "Confidential examination",
            "Confidential assessment",
            "Confidential treatment plan");
        var noteUpdate = await clinician.PutAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter!.Id:D}/note", note);
        Assert.Equal(HttpStatusCode.OK, noteUpdate.StatusCode);

        var requests = new (string Action, HttpRequestMessage Request)[]
        {
            ("GetOpdEncounter", new HttpRequestMessage(HttpMethod.Get,
                $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}")),
            ("GetOpdEncounterByQueueEntry", new HttpRequestMessage(HttpMethod.Get,
                $"/api/v1/hospitals/{hospitalId:D}/opd/queue/{queueEntry.Id:D}/encounter")),
            ("CreateOpdEncounterFromQueue", new HttpRequestMessage(HttpMethod.Post,
                $"/api/v1/hospitals/{hospitalId:D}/opd/queue/{queueEntry.Id:D}/encounter")),
            ("UpdateOpdEncounterNote", new HttpRequestMessage(HttpMethod.Put,
                $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/note")
                { Content = JsonContent.Create(note with { Assessment = "Unauthorized alteration" }) }),
            ("SignOpdEncounter", new HttpRequestMessage(HttpMethod.Post,
                $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/sign")),
            ("AddOpdEncounterAmendment", new HttpRequestMessage(HttpMethod.Post,
                $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/amendments")
                { Content = JsonContent.Create(new OpdEncounterAmendmentInput("Unauthorized", "Unauthorized amendment")) }),
            ("CloseOpdEncounter", new HttpRequestMessage(HttpMethod.Post,
                $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/close")),
            ("CancelOpdEncounter", new HttpRequestMessage(HttpMethod.Post,
                $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/cancel")
                { Content = JsonContent.Create(new OpdEncounterCancelInput("Unauthorized cancellation")) })
        };

        foreach (var (action, request) in requests)
        {
            using (request)
            using (var response = await restricted.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(privateClinicalText, body, StringComparison.Ordinal);
                Assert.DoesNotContain("Confidential patient history", body, StringComparison.Ordinal);
                Assert.DoesNotContain("Confidential assessment", body, StringComparison.Ordinal);
            }
        }

        var ownerRead = await clinician.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}");
        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        var unchanged = await ownerRead.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(unchanged);
        Assert.Equal(OpdEncounterStatuses.InProgress, unchanged!.Status);
        Assert.Equal(privateClinicalText, unchanged.ChiefComplaint);
        Assert.Equal("Confidential assessment", unchanged.Assessment);
        Assert.Empty(unchanged.Amendments);
        Assert.Null(unchanged.SignedAtUtc);
        Assert.Null(unchanged.ClosedAtUtc);
        Assert.Null(unchanged.CancellationReason);

        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var deniedAudits = await auditDb.AuditEvents.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId &&
                item.ActorSubject == restrictedSubject &&
                item.ResourceType == "OPD encounters" &&
                item.Outcome == "Denied")
            .OrderBy(item => item.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(requests.Length, deniedAudits.Count);
        Assert.All(deniedAudits, audit =>
        {
            Assert.Equal(tenantId, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal(restrictedSubject, audit.ActorSubject);
            Assert.Equal((int)HttpStatusCode.Forbidden, audit.StatusCode);
        });
        foreach (var action in requests.Select(item => item.Action))
        {
            Assert.Contains(deniedAudits, audit => audit.Action == action);
        }
    }

    private async Task<(Guid HospitalId, Guid DepartmentId, Guid PatientId)> SeedPatientAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "OPD authorization tenant",
            Slug = $"opdauth-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital
        {
            Name = "OPD Authorization Hospital",
            Code = Guid.NewGuid().ToString("N")[..8]
        };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();

        var department = new Department { HospitalId = hospital.Id, Name = "Medicine", Code = "MED" };
        var patient = new Patient { GivenName = "Ravi", FamilyName = "Deshmukh" };
        db.Departments.Add(department);
        db.Patients.Add(patient);
        db.HospitalPatientRegistrations.Add(new HospitalPatientRegistration
        {
            PatientId = patient.Id,
            HospitalId = hospital.Id,
            RegisteredAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return (hospital.Id, department.Id, patient.Id);
    }

    private HttpClient CreateClient(Guid tenantId, string subject)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken(subject));
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
    }
}
