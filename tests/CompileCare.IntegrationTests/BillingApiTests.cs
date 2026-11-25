using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Billing;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class BillingApiTests
{
    private readonly FacilityApiFactory _factory;

    public BillingApiTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task HospitalAdminCanManageCatalogueAndRegistrarCanIssueInvoiceTakePartialPaymentsAndReadReceipt()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, _, queueEntryId, patientId) = await SeedVisitAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "billing-reception-user");

        using var admin = CreateClient(tenantId, TestJwtTokenFactory.Subject);
        using var registrar = CreateClient(tenantId, "billing-reception-user");

        var serviceResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/service-items",
            new BillingServiceItemInput("CONSULT", "OPD Consultation", "Consultation", 500m));
        Assert.Equal(HttpStatusCode.Created, serviceResponse.StatusCode);
        var service = await serviceResponse.Content.ReadFromJsonAsync<BillingServiceItemDto>();
        Assert.NotNull(service);

        var invoiceResponse = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices",
            new InvoiceCreateInput(queueEntryId, [new InvoiceLineInput(service!.Id, 1)]));
        Assert.Equal(HttpStatusCode.Created, invoiceResponse.StatusCode);
        var invoice = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        Assert.NotNull(invoice);
        Assert.Equal(500m, invoice!.Subtotal);
        Assert.Equal(patientId, invoice.PatientId);
        Assert.Equal("OPD Consultation", Assert.Single(invoice.Lines).Description);
        var invoiceByVisit = await registrar.GetFromJsonAsync<InvoiceDto>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/visits/{queueEntryId:D}/invoice");
        Assert.NotNull(invoiceByVisit);
        Assert.Equal(invoice.Id, invoiceByVisit!.Id);

        var duplicateInvoiceResponse = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices",
            new InvoiceCreateInput(queueEntryId, [new InvoiceLineInput(service.Id, 1)]));
        Assert.Equal(HttpStatusCode.Conflict, duplicateInvoiceResponse.StatusCode);

        var partialPaymentResponse = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice.Id:D}/payments",
            new PaymentCreateInput(200m, PaymentMethods.Cash, null));
        Assert.True(partialPaymentResponse.StatusCode == HttpStatusCode.Created,
            $"Partial payment failed: {(int)partialPaymentResponse.StatusCode} {await partialPaymentResponse.Content.ReadAsStringAsync()}");
        var partialPayment = await partialPaymentResponse.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(partialPayment);
        var partiallySettledInvoice = await registrar.GetFromJsonAsync<InvoiceDto>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice.Id:D}");
        Assert.NotNull(partiallySettledInvoice);
        Assert.Equal(InvoiceStatuses.PartiallyPaid, partiallySettledInvoice!.Status);
        Assert.Equal(200m, partiallySettledInvoice.AmountPaid);
        Assert.Equal(300m, partiallySettledInvoice.BalanceDue);

        var finalPaymentResponse = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice.Id:D}/payments",
            new PaymentCreateInput(300m, PaymentMethods.Upi, "UPI-REF-001"));
        Assert.Equal(HttpStatusCode.Created, finalPaymentResponse.StatusCode);
        var finalPayment = await finalPaymentResponse.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(finalPayment);

        var overpayment = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice.Id:D}/payments",
            new PaymentCreateInput(1m, PaymentMethods.Cash, null));
        Assert.Equal(HttpStatusCode.Conflict, overpayment.StatusCode);

        var refreshedInvoice = await registrar.GetFromJsonAsync<InvoiceDto>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice.Id:D}");
        Assert.NotNull(refreshedInvoice);
        Assert.Equal(500m, refreshedInvoice!.AmountPaid);
        Assert.Equal(0m, refreshedInvoice.BalanceDue);
        Assert.Equal(InvoiceStatuses.Paid, refreshedInvoice.Status);

        var receipt = await registrar.GetFromJsonAsync<ReceiptDto>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/payments/{finalPayment!.Id:D}/receipt");
        Assert.NotNull(receipt);
        Assert.Equal(finalPayment.ReceiptNumber, receipt!.ReceiptNumber);
        Assert.Equal(300m, receipt.Amount);
        Assert.Equal("UPI-REF-001", receipt.Reference);

        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var businessDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var report = await registrar.GetFromJsonAsync<DailyCollectionReportDto>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/reports/daily-collection?businessDate={businessDate:yyyy-MM-dd}&timeZoneId=Asia%2FKolkata");
        Assert.NotNull(report);
        Assert.Equal(500m, report!.TotalCollected);
        Assert.Equal(2, report.PaymentCount);
        Assert.Equal(200m, report.ByMethod[PaymentMethods.Cash]);
        Assert.Equal(300m, report.ByMethod[PaymentMethods.Upi]);
    }

    [Fact]
    public async Task FacilityManagerCannotReadOrManageBilling()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, _, queueEntryId, _) = await SeedVisitAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.FacilityManager,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        using var manager = CreateClient(tenantId, TestJwtTokenFactory.Subject);

        var invoice = await manager.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices",
            new InvoiceCreateInput(queueEntryId, []));
        var catalogue = await manager.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/service-items");

        Assert.Equal(HttpStatusCode.Forbidden, invoice.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, catalogue.StatusCode);
    }

    [Fact]
    public async Task DepartmentScopedRegistrarCannotReadInvoiceForAnotherDepartment()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, queueEntryId, _) = await SeedVisitAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);

        using var admin = CreateClient(tenantId, TestJwtTokenFactory.Subject);
        var serviceResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/service-items",
            new BillingServiceItemInput("CONSULT", "OPD Consultation", "Consultation", 500m));
        Assert.Equal(HttpStatusCode.Created, serviceResponse.StatusCode);
        var service = await serviceResponse.Content.ReadFromJsonAsync<BillingServiceItemDto>();
        Assert.NotNull(service);
        var invoiceResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices",
            new InvoiceCreateInput(queueEntryId, [new InvoiceLineInput(service!.Id, 1)]));
        Assert.Equal(HttpStatusCode.Created, invoiceResponse.StatusCode);

        Guid unrelatedDepartmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
            var unrelatedDepartment = new Department
            {
                HospitalId = hospitalId, Name = "Neurology", Code = "NEUR"
            };
            db.Departments.Add(unrelatedDepartment);
            await db.SaveChangesAsync();
            unrelatedDepartmentId = unrelatedDepartment.Id;
        }

        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, unrelatedDepartmentId)],
            subject: "billing-other-department");
        using var otherDepartmentRegistrar = CreateClient(tenantId, "billing-other-department");
        var response = await otherDepartmentRegistrar.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/visits/{queueEntryId:D}/invoice");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentPaymentsCannotOverpayAnInvoice()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, _, queueEntryId, _) = await SeedVisitAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "billing-cashier-one");
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "billing-cashier-two");

        using var admin = CreateClient(tenantId, TestJwtTokenFactory.Subject);
        using var cashierOne = CreateClient(tenantId, "billing-cashier-one");
        using var cashierTwo = CreateClient(tenantId, "billing-cashier-two");
        var serviceResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/service-items",
            new BillingServiceItemInput("CONSULT", "OPD Consultation", "Consultation", 500m));
        Assert.Equal(HttpStatusCode.Created, serviceResponse.StatusCode);
        var service = await serviceResponse.Content.ReadFromJsonAsync<BillingServiceItemDto>();
        Assert.NotNull(service);
        var invoiceResponse = await cashierOne.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices",
            new InvoiceCreateInput(queueEntryId, [new InvoiceLineInput(service!.Id, 1)]));
        Assert.Equal(HttpStatusCode.Created, invoiceResponse.StatusCode);
        var invoice = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        Assert.NotNull(invoice);

        var firstPayment = cashierOne.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice!.Id:D}/payments",
            new PaymentCreateInput(300m, PaymentMethods.Cash, "CASHIER-ONE"));
        var secondPayment = cashierTwo.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice.Id:D}/payments",
            new PaymentCreateInput(300m, PaymentMethods.Upi, "CASHIER-TWO"));
        var responses = await Task.WhenAll(firstPayment, secondPayment);

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var refreshed = await cashierOne.GetFromJsonAsync<InvoiceDto>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice.Id:D}");
        Assert.NotNull(refreshed);
        Assert.Equal(300m, refreshed!.AmountPaid);
        Assert.Equal(200m, refreshed.BalanceDue);
        Assert.Equal(InvoiceStatuses.PartiallyPaid, refreshed.Status);
    }

    private async Task<(Guid HospitalId, Guid DepartmentId, Guid QueueEntryId, Guid PatientId)> SeedVisitAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Billing test tenant", Slug = $"billing-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital { Name = "Billing Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();
        var department = new Department { HospitalId = hospital.Id, Name = "Medicine", Code = "MED" };
        db.Departments.Add(department);
        var patient = new Patient { GivenName = "Asha", FamilyName = "Patil" };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        db.HospitalPatientRegistrations.Add(new HospitalPatientRegistration
        {
            HospitalId = hospital.Id, PatientId = patient.Id, RegisteredAtUtc = DateTimeOffset.UtcNow
        });
        var queue = new QueueEntry
        {
            HospitalId = hospital.Id, DepartmentId = department.Id, PatientId = patient.Id,
            QueueDateLocal = new DateOnly(2026, 11, 24), TimeZoneId = "Asia/Kolkata",
            TokenNumber = 1, Status = "Completed", CheckedInAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
        db.QueueEntries.Add(queue);
        await db.SaveChangesAsync();
        return (hospital.Id, department.Id, queue.Id, patient.Id);
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
