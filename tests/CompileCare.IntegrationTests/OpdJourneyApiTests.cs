using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Appointments;
using CompileCare.Application.Billing;
using CompileCare.Application.Clinical;
using CompileCare.Application.Patients;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompileCare.IntegrationTests;

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
    }

    private async Task<(Guid HospitalId, Guid DepartmentId)> SeedHospitalAndDepartmentAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();

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
