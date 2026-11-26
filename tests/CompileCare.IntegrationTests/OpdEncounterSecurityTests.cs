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

        var clinicianPatient = await clinician.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/{patientId:D}");
        Assert.Equal(HttpStatusCode.OK, clinicianPatient.StatusCode);
        var forbiddenPatientSearch = await clinician.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients?query=Ravi&page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenPatientSearch.StatusCode);

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
    public async Task ClinicalUserCannotReadCreateOrModifyEncountersOutsideAssignedHospitalScope()
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

        const string originalClinicalText = "HospitalB-Private-Clinical-Note";
        var originalNote = new OpdEncounterNoteInput(
            originalClinicalText,
            "Hospital B private history",
            "Hospital B private examination",
            "Hospital B private assessment",
            "Hospital B private plan");
        var ownerUpdate = await clinicianB.PutAsJsonAsync(
            $"/api/v1/hospitals/{hospitalB:D}/opd/encounters/{encounter!.Id:D}/note",
            originalNote);
        Assert.Equal(HttpStatusCode.OK, ownerUpdate.StatusCode);

        var crossHospitalPatientRead = await clinicianA.GetAsync(
            $"/api/v1/hospitals/{hospitalB:D}/patients/{patientId:D}");
        Assert.Equal(HttpStatusCode.NotFound, crossHospitalPatientRead.StatusCode);

        var crossHospitalRead = await clinicianA.GetAsync(
            $"/api/v1/hospitals/{hospitalB:D}/opd/encounters/{encounter.Id:D}");
        Assert.Equal(HttpStatusCode.NotFound, crossHospitalRead.StatusCode);
        var crossHospitalQueueRead = await clinicianA.GetAsync(
            $"/api/v1/hospitals/{hospitalB:D}/opd/queue/{queueEntry.Id:D}/encounter");
        Assert.Equal(HttpStatusCode.NotFound, crossHospitalQueueRead.StatusCode);
        var crossHospitalUpdate = await clinicianA.PutAsJsonAsync(
            $"/api/v1/hospitals/{hospitalB:D}/opd/encounters/{encounter.Id:D}/note",
            originalNote with { Assessment = "Unauthorized Hospital A modification" });
        Assert.Equal(HttpStatusCode.NotFound, crossHospitalUpdate.StatusCode);
        var crossHospitalSign = await clinicianA.PostAsync(
            $"/api/v1/hospitals/{hospitalB:D}/opd/encounters/{encounter.Id:D}/sign", null);
        Assert.Equal(HttpStatusCode.NotFound, crossHospitalSign.StatusCode);
        var crossHospitalCreate = await clinicianA.PostAsync(
            $"/api/v1/hospitals/{hospitalB:D}/opd/queue/{queueEntry.Id:D}/encounter", null);
        Assert.Equal(HttpStatusCode.NotFound, crossHospitalCreate.StatusCode);

        var ownerRead = await clinicianB.GetAsync(
            $"/api/v1/hospitals/{hospitalB:D}/opd/encounters/{encounter.Id:D}");
        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        var unchanged = await ownerRead.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(unchanged);
        Assert.Equal(originalClinicalText, unchanged!.ChiefComplaint);
        Assert.Equal("Hospital B private assessment", unchanged.Assessment);
        Assert.Null(unchanged.SignedAtUtc);

        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var deniedEncounterAudits = await auditDb.AuditEvents.AsNoTracking()
            .Where(audit =>
                audit.TenantId == tenantId &&
                audit.ActorSubject == "scope-clinician-a" &&
                audit.ResourceType == "OPD encounters")
            .OrderBy(audit => audit.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(5, deniedEncounterAudits.Count);
        Assert.All(deniedEncounterAudits, audit =>
        {
            Assert.Equal(tenantId, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal("scope-clinician-a", audit.ActorSubject);
            Assert.Equal("Rejected", audit.Outcome);
            Assert.Equal((int)HttpStatusCode.NotFound, audit.StatusCode);
        });
        Assert.Contains(deniedEncounterAudits, audit => audit.Action == "GetOpdEncounter");
        Assert.Contains(deniedEncounterAudits, audit => audit.Action == "GetOpdEncounterByQueueEntry");
        Assert.Contains(deniedEncounterAudits, audit => audit.Action == "UpdateOpdEncounterNote");
        Assert.Contains(deniedEncounterAudits, audit => audit.Action == "SignOpdEncounter");
        Assert.Contains(deniedEncounterAudits, audit => audit.Action == "CreateOpdEncounterFromQueue");
    }

    [Fact]
    public async Task DepartmentScopedClinicalUserCannotReadCreateOrModifyEncountersOutsideAssignedDepartment()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentA, patientId) = await SeedPatientAsync(tenantId);
        Guid departmentB;
        using (var seedScope = _factory.Services.CreateScope())
        {
            seedScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = seedScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
            var department = new Department
            {
                HospitalId = hospitalId,
                Name = "Surgery",
                Code = "SUR"
            };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
            departmentB = department.Id;
        }

        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, departmentB)],
            subject: "department-frontdesk-b");
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.ClinicalUser,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, departmentB)],
            subject: "department-clinician-b");
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.ClinicalUser,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, departmentA)],
            subject: "department-clinician-a");

        using var registrarB = CreateClient(tenantId, "department-frontdesk-b");
        using var clinicianB = CreateClient(tenantId, "department-clinician-b");
        using var clinicianA = CreateClient(tenantId, "department-clinician-a");

        var walkIn = await registrarB.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/walk-ins",
            new WalkInCreateInput(departmentB, patientId, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, walkIn.StatusCode);
        var queueEntry = await walkIn.Content.ReadFromJsonAsync<QueueEntryDto>();
        Assert.NotNull(queueEntry);

        var call = await registrarB.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{queueEntry!.Id:D}/call",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        var start = await registrarB.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{queueEntry.Id:D}/start",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var create = await clinicianB.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/queue/{queueEntry.Id:D}/encounter", null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var encounter = await create.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(encounter);

        const string originalClinicalText = "DepartmentB-Private-Clinical-Note";
        var originalNote = new OpdEncounterNoteInput(
            originalClinicalText,
            "Department B private history",
            "Department B private examination",
            "Department B private assessment",
            "Department B private plan");
        var ownerUpdate = await clinicianB.PutAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter!.Id:D}/note",
            originalNote);
        Assert.Equal(HttpStatusCode.OK, ownerUpdate.StatusCode);

        var crossDepartmentRead = await clinicianA.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}");
        Assert.Equal(HttpStatusCode.NotFound, crossDepartmentRead.StatusCode);
        var crossDepartmentQueueRead = await clinicianA.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/queue/{queueEntry.Id:D}/encounter");
        Assert.Equal(HttpStatusCode.NotFound, crossDepartmentQueueRead.StatusCode);
        var crossDepartmentUpdate = await clinicianA.PutAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/note",
            originalNote with { Assessment = "Unauthorized Department A modification" });
        Assert.Equal(HttpStatusCode.NotFound, crossDepartmentUpdate.StatusCode);
        var crossDepartmentSign = await clinicianA.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/sign", null);
        Assert.Equal(HttpStatusCode.NotFound, crossDepartmentSign.StatusCode);
        var crossDepartmentCreate = await clinicianA.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/queue/{queueEntry.Id:D}/encounter", null);
        Assert.Equal(HttpStatusCode.NotFound, crossDepartmentCreate.StatusCode);

        var ownerRead = await clinicianB.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}");
        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        var unchanged = await ownerRead.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(unchanged);
        Assert.Equal(originalClinicalText, unchanged!.ChiefComplaint);
        Assert.Equal("Department B private assessment", unchanged.Assessment);
        Assert.Null(unchanged.SignedAtUtc);

        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var deniedEncounterAudits = await auditDb.AuditEvents.AsNoTracking()
            .Where(audit =>
                audit.TenantId == tenantId &&
                audit.ActorSubject == "department-clinician-a" &&
                audit.ResourceType == "OPD encounters")
            .OrderBy(audit => audit.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(5, deniedEncounterAudits.Count);
        Assert.All(deniedEncounterAudits, audit =>
        {
            Assert.Equal(tenantId, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal("department-clinician-a", audit.ActorSubject);
            Assert.Equal("Rejected", audit.Outcome);
            Assert.Equal((int)HttpStatusCode.NotFound, audit.StatusCode);
        });
        Assert.Contains(deniedEncounterAudits, audit => audit.Action == "GetOpdEncounter");
        Assert.Contains(deniedEncounterAudits, audit => audit.Action == "GetOpdEncounterByQueueEntry");
        Assert.Contains(deniedEncounterAudits, audit => audit.Action == "UpdateOpdEncounterNote");
        Assert.Contains(deniedEncounterAudits, audit => audit.Action == "SignOpdEncounter");
        Assert.Contains(deniedEncounterAudits, audit => audit.Action == "CreateOpdEncounterFromQueue");
    }

    [Fact]
    public async Task CrossTenantClinicalUserCannotReadOrModifyEncounterAndDenialsAreAudited()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (hospitalA, departmentA, patientA) = await SeedPatientAsync(tenantA);
        var (hospitalB, _, _) = await SeedPatientAsync(tenantB);

        const string registrarA = "cross-tenant-opd-frontdesk-a";
        const string clinicianA = "cross-tenant-opd-clinician-a";
        const string clinicianB = "cross-tenant-opd-clinician-b";

        await _factory.SeedTenantMembershipAsync(
            tenantA, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalA)],
            subject: registrarA);
        await _factory.SeedTenantMembershipAsync(
            tenantA, TenantRoles.ClinicalUser,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalA)],
            subject: clinicianA);
        await _factory.SeedTenantMembershipAsync(
            tenantB, TenantRoles.ClinicalUser,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalB)],
            subject: clinicianB);

        using var registrar = CreateClient(tenantA, registrarA);
        using var clinician = CreateClient(tenantA, clinicianA);
        using var crossTenantClinician = CreateClient(tenantB, clinicianB);

        var walkIn = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}/queue/walk-ins",
            new WalkInCreateInput(departmentA, patientA, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, walkIn.StatusCode);
        var queueEntry = await walkIn.Content.ReadFromJsonAsync<QueueEntryDto>();
        Assert.NotNull(queueEntry);

        var call = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}/queue/{queueEntry!.Id:D}/call",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        var start = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}/queue/{queueEntry.Id:D}/start",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var create = await clinician.PostAsync(
            $"/api/v1/hospitals/{hospitalA:D}/opd/queue/{queueEntry.Id:D}/encounter", null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var encounter = await create.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(encounter);

        const string clinicalText = "TenantA-Confidential-Clinical-Note-Headache";
        var originalNote = new OpdEncounterNoteInput(
            clinicalText,
            "Tenant A private history",
            "Tenant A private examination",
            "Tenant A private assessment",
            "Tenant A private plan");
        var update = await clinician.PutAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}/opd/encounters/{encounter!.Id:D}/note",
            originalNote);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var billingBase = $"/api/v1/hospitals/{hospitalA:D}/opd";
        var directRead = await crossTenantClinician.GetAsync(
            $"{billingBase}/encounters/{encounter.Id:D}");
        var queueRead = await crossTenantClinician.GetAsync(
            $"{billingBase}/queue/{queueEntry.Id:D}/encounter");
        var attemptedWrite = await crossTenantClinician.PutAsJsonAsync(
            $"{billingBase}/encounters/{encounter.Id:D}/note",
            originalNote with { Assessment = "Unauthorized tenant B change" });

        Assert.Equal(HttpStatusCode.NotFound, directRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, queueRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, attemptedWrite.StatusCode);

        var deniedBodies = await Task.WhenAll(
            directRead.Content.ReadAsStringAsync(),
            queueRead.Content.ReadAsStringAsync(),
            attemptedWrite.Content.ReadAsStringAsync());
        Assert.All(deniedBodies, body =>
        {
            Assert.DoesNotContain(clinicalText, body, StringComparison.Ordinal);
            Assert.DoesNotContain("Tenant A private history", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Tenant A private assessment", body, StringComparison.Ordinal);
        });

        var ownerRead = await clinician.GetAsync(
            $"{billingBase}/encounters/{encounter.Id:D}");
        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        var unchanged = await ownerRead.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(unchanged);
        Assert.Equal(clinicalText, unchanged!.ChiefComplaint);
        Assert.Equal("Tenant A private assessment", unchanged.Assessment);

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantB);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var auditEvents = await db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorSubject == clinicianB)
            .OrderBy(audit => audit.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(3, auditEvents.Count);
        Assert.All(auditEvents, audit =>
        {
            Assert.Equal(tenantB, audit.TenantId);
            Assert.Equal(tenantB, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal(clinicianB, audit.ActorSubject);
            Assert.Equal("OPD encounters", audit.ResourceType);
            Assert.Equal("Rejected", audit.Outcome);
            Assert.Equal((int)HttpStatusCode.NotFound, audit.StatusCode);
        });

        Assert.Contains(auditEvents, audit =>
            audit.Action == "GetOpdEncounter" && audit.ResourceId == encounter.Id);
        Assert.Contains(auditEvents, audit =>
            audit.Action == "GetOpdEncounterByQueueEntry" && audit.ResourceId == queueEntry.Id);
        Assert.Contains(auditEvents, audit =>
            audit.Action == "UpdateOpdEncounterNote" && audit.ResourceId == encounter.Id);
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
