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
public sealed class OpdEncounterSecurityTests
{
    private readonly FacilityApiFactory _factory;

    public OpdEncounterSecurityTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task ClinicalReadsAndWritesAreAuditedWithoutPersistingNoteContentInAuditMetadata()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, patientId) = await SeedPatientAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "audit-frontdesk");
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.ClinicalUser,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "audit-clinician");

        using var registrar = CreateClient(tenantId, "audit-frontdesk");
        using var clinician = CreateClient(tenantId, "audit-clinician");
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

        var clinicianDepartments = await clinician.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/departments?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, clinicianDepartments.StatusCode);

        var clinicianQueue = await clinician.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue?departmentId={departmentId:D}&date={queueEntry.QueueDateLocal:yyyy-MM-dd}&page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.OK, clinicianQueue.StatusCode);
        var clinicianQueuePage = await clinicianQueue.Content.ReadFromJsonAsync<QueuePage>();
        Assert.NotNull(clinicianQueuePage);
        Assert.Contains(clinicianQueuePage!.Items, item => item.Id == queueEntry.Id);

        var forbiddenQueueMutation = await clinician.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{queueEntry.Id:D}/complete",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenQueueMutation.StatusCode);

        var create = await clinician.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/queue/{queueEntry.Id:D}/encounter", null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var encounter = await create.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(encounter);

        var resumed = await clinician.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/queue/{queueEntry.Id:D}/encounter");
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        var resumedEncounter = await resumed.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.Equal(encounter!.Id, resumedEncounter!.Id);

        const string clinicalText = "SensitiveClinicalNote-Headache-OnlyInClinicalRecord";
        var update = await clinician.PutAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter!.Id:D}/note",
            new OpdEncounterNoteInput(clinicalText, "Private history", "Private examination", "Private assessment", "Private plan"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var sign = await clinician.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/sign", null);
        Assert.Equal(HttpStatusCode.OK, sign.StatusCode);
        var read = await clinician.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var denied = await registrar.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var events = await db.AuditEvents.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ResourceType == "OPD encounters")
            .ToListAsync();

        Assert.True(events.Count >= 5);
        Assert.Contains(events, x => x.ActorSubject == "audit-clinician" && x.HttpMethod == "POST");
        Assert.Contains(events, x => x.ActorSubject == "audit-clinician" && x.HttpMethod == "PUT");
        Assert.Contains(events, x => x.ActorSubject == "audit-clinician" && x.HttpMethod == "GET");
        Assert.Contains(events, x => x.ActorSubject == "audit-frontdesk" && x.Outcome == "Denied");
        var auditMetadata = string.Join(" ", events.SelectMany(x => new[]
        {
            x.Action, x.ResourceType, x.RouteTemplate, x.CorrelationId,
            x.ActorIssuer ?? string.Empty, x.ActorSubject ?? string.Empty
        }));
        Assert.False(auditMetadata.Contains(clinicalText, StringComparison.OrdinalIgnoreCase));
        Assert.False(auditMetadata.Contains("Private history", StringComparison.OrdinalIgnoreCase));
        Assert.False(auditMetadata.Contains("Private examination", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ClinicalUserCannotReadOrCreateEncountersOutsideAssignedHospitalScope()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalA, _, patientId) = await SeedPatientAsync(tenantId);
        Guid hospitalB;
        Guid departmentB;
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
            var hospital = new Hospital { Name = "Second OPD Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
            db.Hospitals.Add(hospital);
            await db.SaveChangesAsync();
            var department = new Department { HospitalId = hospital.Id, Name = "Surgery", Code = "SUR" };
            db.Departments.Add(department);
            db.HospitalPatientRegistrations.Add(new HospitalPatientRegistration
            {
                PatientId = patientId,
                HospitalId = hospital.Id,
                RegisteredAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            hospitalB = hospital.Id;
            departmentB = department.Id;
        }

        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalB)],
            subject: "scope-frontdesk-b");
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.ClinicalUser,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalB)],
            subject: "scope-clinician-b");
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.ClinicalUser,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalA)],
            subject: "scope-clinician-a");

        using var registrarB = CreateClient(tenantId, "scope-frontdesk-b");
        using var clinicianB = CreateClient(tenantId, "scope-clinician-b");
        using var clinicianA = CreateClient(tenantId, "scope-clinician-a");
        var walkIn = await registrarB.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalB:D}/queue/walk-ins",
            new WalkInCreateInput(departmentB, patientId, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, walkIn.StatusCode);
        var queueEntry = await walkIn.Content.ReadFromJsonAsync<QueueEntryDto>();
        Assert.NotNull(queueEntry);

        var call = await registrarB.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalB:D}/queue/{queueEntry!.Id:D}/call",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        var start = await registrarB.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalB:D}/queue/{queueEntry.Id:D}/start",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var create = await clinicianB.PostAsync(
            $"/api/v1/hospitals/{hospitalB:D}/opd/queue/{queueEntry.Id:D}/encounter", null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var encounter = await create.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(encounter);

        var crossHospitalRead = await clinicianA.GetAsync(
            $"/api/v1/hospitals/{hospitalB:D}/opd/encounters/{encounter!.Id:D}");
        Assert.Equal(HttpStatusCode.NotFound, crossHospitalRead.StatusCode);
        var crossHospitalCreate = await clinicianA.PostAsync(
            $"/api/v1/hospitals/{hospitalB:D}/opd/queue/{queueEntry.Id:D}/encounter", null);
        Assert.Equal(HttpStatusCode.NotFound, crossHospitalCreate.StatusCode);
    }

    private async Task<(Guid HospitalId, Guid DepartmentId, Guid PatientId)> SeedPatientAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "OPD security test tenant",
            Slug = $"opdsec-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital { Name = "OPD Security Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();
        var department = new Department { HospitalId = hospital.Id, Name = "Medicine", Code = "MED" };
        db.Departments.Add(department);
        var patient = new Patient { GivenName = "Ravi", FamilyName = "Deshmukh" };
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
