using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HeapCare.Api.Middleware;
using HeapCare.Application.Appointments;
using HeapCare.Application.Billing;
using HeapCare.Application.Clinical;
using HeapCare.Application.Patients;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class OpdJourneyApiTests
{
    private readonly FacilityApiFactory _factory;

    public OpdJourneyApiTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task ReceptionToClinicalToBillingWalkInJourneyCompletesWithScopedIdentityAndReceipt()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId) = await SeedHospitalAndDepartmentAsync(tenantId);

        const string receptionSubject = "journey-reception";
        const string clinicalSubject = "journey-clinician";
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: receptionSubject);
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.ClinicalUser,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, departmentId)],
            subject: clinicalSubject);

        using var admin = CreateClient(tenantId, TestJwtTokenFactory.Subject);
        using var reception = CreateClient(tenantId, receptionSubject);
        using var clinician = CreateClient(tenantId, clinicalSubject);

        // Hospital setup: the administrator defines an MRN policy and a billable tariff.
        var policyResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patient-identifier-policy",
            new PatientIdentifierPolicyInput("CC", 6));
        Assert.Equal(HttpStatusCode.Created, policyResponse.StatusCode);

        var serviceResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/service-items",
            new BillingServiceItemInput("OPD-CONSULT", "OPD Consultation", "Consultation", 500m));
        Assert.Equal(HttpStatusCode.Created, serviceResponse.StatusCode);
        var service = await serviceResponse.Content.ReadFromJsonAsync<BillingServiceItemDto>();
        Assert.NotNull(service);

        // Reception registers the patient, assigns a hospital-local MRN and creates a walk-in.
        var registrationResponse = await reception.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients",
            new PatientRegistrationInput("Asha", "R.", "Patil", new DateOnly(1990, 4, 12)));
        Assert.Equal(HttpStatusCode.Created, registrationResponse.StatusCode);
        var patient = await registrationResponse.Content.ReadFromJsonAsync<PatientDto>();
        Assert.NotNull(patient);
        Assert.Equal(hospitalId, patient!.HospitalId);

        var mrnResponse = await reception.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/{patient.Id:D}/identifiers/mrn",
            content: null);
        Assert.Equal(HttpStatusCode.OK, mrnResponse.StatusCode);
        var mrn = await mrnResponse.Content.ReadFromJsonAsync<PatientIdentifierDto>();
        Assert.NotNull(mrn);
        Assert.StartsWith("CC", mrn!.Value);

        var walkInResponse = await reception.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/walk-ins",
            new WalkInCreateInput(departmentId, patient.Id, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, walkInResponse.StatusCode);
        var queueEntry = await walkInResponse.Content.ReadFromJsonAsync<QueueEntryDto>();
        Assert.NotNull(queueEntry);
        Assert.Equal(departmentId, queueEntry!.DepartmentId);
        Assert.Equal("Waiting", queueEntry.Status);

        // Queue operations remain with reception; clinical access is read-only and department-scoped.
        var callResponse = await reception.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{queueEntry.Id:D}/call",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, callResponse.StatusCode);

        var startResponse = await reception.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{queueEntry.Id:D}/start",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

        var queueDate = queueEntry.QueueDateLocal.ToString("yyyy-MM-dd");
        var clinicalQueue = await clinician.GetFromJsonAsync<QueuePage>(
            $"/api/v1/hospitals/{hospitalId:D}/queue?departmentId={departmentId:D}&date={queueDate}&page=1&pageSize=25");
        Assert.NotNull(clinicalQueue);
        Assert.Contains(clinicalQueue!.Items, item => item.Id == queueEntry.Id);

        var departmentPatient = await clinician.GetFromJsonAsync<PatientDto>(
            $"/api/v1/hospitals/{hospitalId:D}/patients/departments/{departmentId:D}/patients/{patient.Id:D}");
        Assert.NotNull(departmentPatient);
        Assert.Equal(patient.Id, departmentPatient!.Id);

        // Clinical documentation is attached to the queue visit and attributed to the clinician.
        var encounterResponse = await clinician.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/queue/{queueEntry.Id:D}/encounter",
            content: null);
        Assert.Equal(HttpStatusCode.Created, encounterResponse.StatusCode);
        var encounter = await encounterResponse.Content.ReadFromJsonAsync<OpdEncounterDto>();
        Assert.NotNull(encounter);
        Assert.Equal(patient.Id, encounter!.PatientId);
        Assert.Equal(queueEntry.Id, encounter.QueueEntryId);
        Assert.Equal(clinicalSubject, encounter.CreatedBySubject);

        var noteResponse = await clinician.PutAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/note",
            new OpdEncounterNoteInput(
                "Headache",
                "Patient reports intermittent headache.",
                "Examination recorded by clinician.",
                "Clinical assessment documented.",
                "Follow-up as clinically appropriate."));
        Assert.Equal(HttpStatusCode.OK, noteResponse.StatusCode);

        var signResponse = await clinician.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/sign",
            content: null);
        Assert.Equal(HttpStatusCode.OK, signResponse.StatusCode);

        var closeEncounterResponse = await clinician.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/opd/encounters/{encounter.Id:D}/close",
            content: null);
        Assert.Equal(HttpStatusCode.OK, closeEncounterResponse.StatusCode);

        // Reception completes the queue visit before billing.
        var completeQueueResponse = await reception.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{queueEntry.Id:D}/complete",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, completeQueueResponse.StatusCode);
        var completedQueue = await completeQueueResponse.Content.ReadFromJsonAsync<QueueEntryDto>();
        Assert.NotNull(completedQueue);
        Assert.Equal("Completed", completedQueue!.Status);

        // Billing is visit-linked; the existing-invoice lookup supports resuming the same workflow.
        var invoiceResponse = await reception.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices",
            new InvoiceCreateInput(queueEntry.Id, [new InvoiceLineInput(service!.Id, 1)]));
        Assert.Equal(HttpStatusCode.Created, invoiceResponse.StatusCode);
        var invoice = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        Assert.NotNull(invoice);
        Assert.Equal(patient.Id, invoice!.PatientId);
        Assert.Equal(500m, invoice.Subtotal);

        var invoiceByVisit = await reception.GetFromJsonAsync<InvoiceDto>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/visits/{queueEntry.Id:D}/invoice");
        Assert.NotNull(invoiceByVisit);
        Assert.Equal(invoice.Id, invoiceByVisit!.Id);

        var paymentResponse = await reception.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice.Id:D}/payments",
            new PaymentCreateInput(500m, PaymentMethods.Upi, "JOURNEY-UPI-001"));
        Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);
        var payment = await paymentResponse.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(payment);

        var settledInvoice = await reception.GetFromJsonAsync<InvoiceDto>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/visits/{queueEntry.Id:D}/invoice");
        Assert.NotNull(settledInvoice);
        Assert.Equal(InvoiceStatuses.Paid, settledInvoice!.Status);
        Assert.Equal(0m, settledInvoice.BalanceDue);

        var paymentHistory = await reception.GetFromJsonAsync<List<PaymentDto>>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice.Id:D}/payments");
        Assert.NotNull(paymentHistory);
        Assert.Single(paymentHistory!);
        Assert.Equal(payment.Id, paymentHistory[0].Id);

        var receipt = await reception.GetFromJsonAsync<ReceiptDto>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/payments/{payment!.Id:D}/receipt");
        Assert.NotNull(receipt);
        Assert.Equal(payment.ReceiptNumber, receipt!.ReceiptNumber);
        Assert.Equal(500m, receipt.Amount);
        Assert.Equal(PaymentMethods.Upi, receipt.Method);

        var report = await reception.GetFromJsonAsync<DailyCollectionReportDto>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/reports/daily-collection?businessDate={queueEntry.QueueDateLocal:yyyy-MM-dd}&timeZoneId=Asia%2FKolkata");
        Assert.NotNull(report);
        Assert.Equal(500m, report!.TotalCollected);
        Assert.Equal(1, report.PaymentCount);

        // Sensitive clinical actions and payment capture must be auditable without
        // copying clinical note text or payment reference data into the audit record.
        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var auditEvents = await auditDb.AuditEvents
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .ToListAsync();

        Assert.Contains(auditEvents, item =>
            item.Action == "GetDepartmentPatient" &&
            item.ResourceId == patient.Id &&
            item.Outcome == "Succeeded");
        Assert.Contains(auditEvents, item =>
            item.Action == "CreateOpdEncounterFromQueue" &&
            item.ResourceId == queueEntry.Id &&
            item.Outcome == "Succeeded");
        Assert.Contains(auditEvents, item =>
            item.Action == "UpdateOpdEncounterNote" &&
            item.ResourceId == encounter.Id &&
            item.Outcome == "Succeeded");
        Assert.Contains(auditEvents, item =>
            item.Action == "SignOpdEncounter" &&
            item.ResourceId == encounter.Id &&
            item.Outcome == "Succeeded");
        Assert.Contains(auditEvents, item =>
            item.Action == "CloseOpdEncounter" &&
            item.ResourceId == encounter.Id &&
            item.Outcome == "Succeeded");
        Assert.Contains(auditEvents, item =>
            item.Action == "RecordBillingPayment" &&
            item.ResourceId == invoice.Id &&
            item.Outcome == "Succeeded");

        var auditJson = JsonSerializer.Serialize(auditEvents);
        Assert.DoesNotContain("Intermittent headache", auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Clinical assessment documented", auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain("JOURNEY-UPI-001", auditJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClinicalUser_CannotReadQueueOutsideAssignedDepartment_AndDenialIsAudited()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, authorizedDepartmentId) = await SeedHospitalAndDepartmentAsync(tenantId);
        var unrelatedDepartmentId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
            db.Departments.Add(new Department
            {
                Id = unrelatedDepartmentId,
                HospitalId = hospitalId,
                Name = "Neurology",
                Code = "NEUR"
            });
            await db.SaveChangesAsync();
        }

        const string actorSubject = "clinical-user-queue-other-department";
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.ClinicalUser,
            scopes:
            [
                new OrganizationScope(
                    OrganizationScopeTypes.Department,
                    hospitalId,
                    unrelatedDepartmentId)
            ],
            subject: actorSubject);

        using var clinician = CreateClient(tenantId, actorSubject);
        var response = await clinician.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue?departmentId={authorizedDepartmentId:D}&date=2026-11-27&page=1&pageSize=25");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("queueEntryId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(authorizedDepartmentId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);

        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var auditEvent = await auditDb.AuditEvents
            .AsNoTracking()
            .SingleAsync(item =>
                item.TenantId == tenantId &&
                item.Action == "ListDepartmentQueue" &&
                item.ActorSubject == actorSubject);

        Assert.Equal(tenantId, auditEvent.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, auditEvent.ActorIssuer);
        Assert.Equal(actorSubject, auditEvent.ActorSubject);
        Assert.Equal(hospitalId, auditEvent.ResourceId);
        Assert.Equal("Rejected", auditEvent.Outcome);
        Assert.Equal((int)HttpStatusCode.NotFound, auditEvent.StatusCode);
    }

    [Fact]
    public async Task DepartmentScopedPatientRegistrar_CannotTransitionQueueOutsideAssignedDepartment_AndDenialIsAudited()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, authorizedDepartmentId) = await SeedHospitalAndDepartmentAsync(tenantId);
        var unrelatedDepartmentId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
            db.Departments.Add(new Department
            {
                Id = unrelatedDepartmentId,
                HospitalId = hospitalId,
                Name = "Neurology",
                Code = "NEUR"
            });
            await db.SaveChangesAsync();
        }

        const string registrarSubject = "queue-registrar-other-department";
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "queue-scope-admin");
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.PatientRegistrar,
            scopes:
            [
                new OrganizationScope(
                    OrganizationScopeTypes.Department,
                    hospitalId,
                    authorizedDepartmentId)
            ],
            subject: registrarSubject);

        using var admin = CreateClient(tenantId, "queue-scope-admin");
        using var registrar = CreateClient(tenantId, registrarSubject);
        var registration = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients",
            new PatientRegistrationInput("Queue", "Scope", "Synthetic", new DateOnly(1990, 1, 2)));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var patient = await registration.Content.ReadFromJsonAsync<PatientDto>();
        Assert.NotNull(patient);

        var authorizedWalkIn = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/walk-ins",
            new WalkInCreateInput(authorizedDepartmentId, patient!.Id, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, authorizedWalkIn.StatusCode);
        var authorizedQueueEntry = await authorizedWalkIn.Content.ReadFromJsonAsync<QueueEntryDto>();
        Assert.NotNull(authorizedQueueEntry);
        Assert.Equal(authorizedDepartmentId, authorizedQueueEntry!.DepartmentId);

        var allowed = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{authorizedQueueEntry.Id:D}/call",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var calledQueueEntry = await allowed.Content.ReadFromJsonAsync<QueueEntryDto>();
        Assert.NotNull(calledQueueEntry);
        Assert.Equal("Called", calledQueueEntry!.Status);

        var walkIn = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/walk-ins",
            new WalkInCreateInput(unrelatedDepartmentId, patient.Id, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, walkIn.StatusCode);
        var queueEntry = await walkIn.Content.ReadFromJsonAsync<QueueEntryDto>();
        Assert.NotNull(queueEntry);
        Assert.Equal(unrelatedDepartmentId, queueEntry!.DepartmentId);

        var denied = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{queueEntry.Id:D}/call",
            new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var auditEvents = await auditDb.AuditEvents
            .AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId &&
                item.Action == "TransitionQueueEntry" &&
                item.ActorSubject == registrarSubject)
            .OrderBy(item => item.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(2, auditEvents.Count);
        var allowedAudit = Assert.Single(auditEvents, item => item.ResourceId == authorizedQueueEntry.Id);
        Assert.Equal(tenantId, allowedAudit.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, allowedAudit.ActorIssuer);
        Assert.Equal(registrarSubject, allowedAudit.ActorSubject);
        Assert.Equal("Succeeded", allowedAudit.Outcome);
        Assert.Equal((int)HttpStatusCode.OK, allowedAudit.StatusCode);

        var deniedAudit = Assert.Single(auditEvents, item => item.ResourceId == queueEntry.Id);
        Assert.Equal(tenantId, deniedAudit.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, deniedAudit.ActorIssuer);
        Assert.Equal(registrarSubject, deniedAudit.ActorSubject);
        Assert.Equal("Rejected", deniedAudit.Outcome);
        Assert.Equal((int)HttpStatusCode.NotFound, deniedAudit.StatusCode);

        var queueAfterDenial = await admin.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue?departmentId={unrelatedDepartmentId:D}&date={queueEntry.QueueDateLocal:yyyy-MM-dd}&page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.OK, queueAfterDenial.StatusCode);
        var unchangedQueue = await queueAfterDenial.Content.ReadFromJsonAsync<QueuePage>();
        Assert.NotNull(unchangedQueue);
        var persistedEntry = Assert.Single(unchangedQueue!.Items);
        Assert.Equal(queueEntry.Id, persistedEntry.Id);
        Assert.Equal("Waiting", persistedEntry.Status);
    }

    [Fact]
    public async Task CrossDepartmentClinicalPatientRead_ReturnsNotFoundAndAuditsRejectedAttempt()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, authorizedDepartmentId) = await SeedHospitalAndDepartmentAsync(tenantId);
        var unrelatedDepartmentId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
            db.Departments.Add(new Department
            {
                Id = unrelatedDepartmentId,
                HospitalId = hospitalId,
                Name = "Neurology",
                Code = "NEUR"
            });
            await db.SaveChangesAsync();
        }

        const string actorSubject = "clinical-user-other-department";
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.ClinicalUser,
            scopes:
            [
                new OrganizationScope(
                    OrganizationScopeTypes.Department,
                    hospitalId,
                    unrelatedDepartmentId)
            ],
            subject: actorSubject);

        using var clinician = CreateClient(tenantId, actorSubject);
        var requestedPatientId = Guid.NewGuid();
        var response = await clinician.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/departments/{authorizedDepartmentId:D}/patients/{requestedPatientId:D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var auditEvent = await auditDb.AuditEvents
            .AsNoTracking()
            .SingleAsync(item =>
                item.TenantId == tenantId &&
                item.Action == "GetDepartmentPatient" &&
                item.ActorSubject == actorSubject);

        Assert.Equal(tenantId, auditEvent.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, auditEvent.ActorIssuer);
        Assert.Equal(requestedPatientId, auditEvent.ResourceId);
        Assert.Equal("Rejected", auditEvent.Outcome);
        Assert.Equal((int)HttpStatusCode.NotFound, auditEvent.StatusCode);
    }

    private async Task<(Guid HospitalId, Guid DepartmentId)> SeedHospitalAndDepartmentAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "End-to-end journey tenant",
            Slug = $"journey-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital
        {
            Name = "End-to-end journey hospital",
            Code = Guid.NewGuid().ToString("N")[..8]
        };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();

        var department = new Department
        {
            HospitalId = hospital.Id,
            Name = "Medicine",
            Code = "MED"
        };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return (hospital.Id, department.Id);
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
