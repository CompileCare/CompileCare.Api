using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Appointments;
using CompileCare.Application.Clinical;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class OpdEncounterApiTests
{
    private readonly FacilityApiFactory _factory;

    public OpdEncounterApiTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task ClinicalUserCanCreateDocumentSignAndAmendEncounterWithoutChangingSignedNote()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, patientId) = await SeedPatientAsync(tenantId);
        const string registrarSubject = "opd-frontdesk-user";
        const string clinicianSubject = "opd-clinical-user";

        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: registrarSubject);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.ClinicalUser,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: clinicianSubject);

        using var registrar = CreateClient(tenantId, registrarSubject);
        using var clinician = CreateClient(tenantId, clinicianSubject);

        var walkIn = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/walk-ins",
            new WalkInCreateInput(departmentId, patientId, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, walkIn.StatusCode);
        var queueEntry = await walkIn.Content.ReadFromJsonAsync<QueueEntryDto>();
        Assert.NotNull(queueEntry);

        var start = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{queueEntry!.Id:D}/start",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var create = await clinician.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/queue/{queueEntry.Id:D}/encounter", null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var encounter = await create.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(encounter);
        Assert.Equal(OpdEncounterStatuses.InProgress, encounter!.Status);
        Assert.Equal(patientId, encounter.PatientId);
        Assert.Equal(queueEntry.Id, encounter.QueueEntryId);
        Assert.Equal(clinicianSubject, encounter.CreatedBySubject);

        var duplicate = await clinician.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/queue/{queueEntry.Id:D}/encounter", null);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var noteInput = new OpdEncounterNoteInput(
            "Headache for two days",
            "Intermittent headache, no additional history recorded.",
            "Patient alert; examination documented by clinician.",
            "Headache; further clinical assessment required.",
            "Follow-up as clinically appropriate.");
        var update = await clinician.PutAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/note", noteInput);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var sign = await clinician.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/sign", null);
        Assert.Equal(HttpStatusCode.OK, sign.StatusCode);
        var signed = await sign.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(signed);
        Assert.Equal(OpdEncounterStatuses.Signed, signed!.Status);
        Assert.Equal(clinicianSubject, signed.SignedBySubject);
        Assert.NotNull(signed.SignedAtUtc);

        var overwrite = await clinician.PutAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/note",
            noteInput with { Assessment = "Changed after signature" });
        Assert.Equal(HttpStatusCode.Conflict, overwrite.StatusCode);

        var close = await clinician.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/close", null);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);

        var amendment = await clinician.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/amendments",
            new OpdEncounterAmendmentInput("Correction", "Additional clarification recorded after signature."));
        Assert.Equal(HttpStatusCode.OK, amendment.StatusCode);
        var amended = await amendment.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(amended);
        Assert.Equal(OpdEncounterStatuses.Closed, amended!.Status);
        Assert.Equal("Headache; further clinical assessment required.", amended.Assessment);
        Assert.Single(amended.Amendments);
        Assert.Equal(clinicianSubject, amended.Amendments[0].AuthorSubject);

        var forbidden = await registrar.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task EncounterCreationRequiresQueueEntryToBeInProgress()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, patientId) = await SeedPatientAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "opd-registrar");
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.ClinicalUser,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "opd-clinician");
        using var registrar = CreateClient(tenantId, "opd-registrar");
        using var clinician = CreateClient(tenantId, "opd-clinician");

        var walkIn = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/walk-ins",
            new WalkInCreateInput(departmentId, patientId, "Asia/Kolkata"));
        var queueEntry = await walkIn.Content.ReadFromJsonAsync<QueueEntryDto>();
        Assert.NotNull(queueEntry);

        var response = await clinician.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/queue/{queueEntry!.Id:D}/encounter", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<(Guid HospitalId, Guid DepartmentId, Guid PatientId)> SeedPatientAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "OPD test tenant",
            Slug = $"opd-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital { Name = "OPD Test Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
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
