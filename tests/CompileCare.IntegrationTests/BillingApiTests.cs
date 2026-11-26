using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Billing;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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

        var paymentHistory = await registrar.GetFromJsonAsync<List<PaymentDto>>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice.Id:D}/payments");
        Assert.NotNull(paymentHistory);
        Assert.Equal(2, paymentHistory!.Count);
        Assert.Contains(paymentHistory, payment => payment.Amount == 200m);
        Assert.Contains(paymentHistory, payment => payment.Amount == 300m);

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

    [Theory]
    [InlineData(TenantRoles.FacilityViewer)]
    [InlineData(TenantRoles.ClinicalUser)]
    public async Task RolesWithoutBillingGrants_CannotReadOrMutateBilling(
        string role)
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, _, queueEntryId, _) = await SeedVisitAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "billing-role-admin");
        await _factory.SeedTenantMembershipAsync(
            tenantId, role,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "billing-role-restricted");

        using var admin = CreateClient(tenantId, "billing-role-admin");
        using var restricted = CreateClient(tenantId, "billing-role-restricted");

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
        var invoice = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        Assert.NotNull(invoice);

        var catalogueRead = await restricted.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/service-items");
        var invoiceRead = await restricted.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice!.Id:D}");
        var invoiceCreate = await restricted.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices",
            new InvoiceCreateInput(queueEntryId, [new InvoiceLineInput(service.Id, 1)]));
        var paymentCreate = await restricted.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice.Id:D}/payments",
            new PaymentCreateInput(100m, PaymentMethods.Cash, null));
        var reportRead = await restricted.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/reports/daily-collection?businessDate=2026-11-24&timeZoneId=Asia%2FKolkata");

        Assert.Equal(HttpStatusCode.Forbidden, catalogueRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, invoiceRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, invoiceCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, paymentCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reportRead.StatusCode);

        var unchanged = await admin.GetFromJsonAsync<InvoiceDto>(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice.Id:D}");
        Assert.NotNull(unchanged);
        Assert.Equal(0m, unchanged!.AmountPaid);
        Assert.Equal(500m, unchanged.BalanceDue);
        Assert.Equal(InvoiceStatuses.Issued, unchanged.Status);

        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var deniedAudits = await auditDb.AuditEvents.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId &&
                item.ActorSubject == "billing-role-restricted" &&
                item.ResourceType == "Billing" &&
                item.Outcome == "Denied")
            .OrderBy(item => item.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(5, deniedAudits.Count);
        Assert.All(deniedAudits, audit =>
        {
            Assert.Equal(tenantId, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal("billing-role-restricted", audit.ActorSubject);
            Assert.Equal((int)HttpStatusCode.Forbidden, audit.StatusCode);
        });
        Assert.Contains(deniedAudits, audit => audit.Action == "ListBillingServiceItems");
        Assert.Contains(deniedAudits, audit => audit.Action == "GetBillingInvoice");
        Assert.Contains(deniedAudits, audit => audit.Action == "CreateBillingInvoice");
        Assert.Contains(deniedAudits, audit => audit.Action == "RecordBillingPayment");
        Assert.Contains(deniedAudits, audit => audit.Action == "GetDailyBillingCollection");
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
        var invoice = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        Assert.NotNull(invoice);

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
        var paymentHistoryResponse = await otherDepartmentRegistrar.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{invoice!.Id:D}/payments");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, paymentHistoryResponse.StatusCode);

        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var deniedBillingAudits = await auditDb.AuditEvents.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId &&
                item.ActorSubject == "billing-other-department" &&
                item.ResourceType == "Billing" &&
                item.Outcome == "Rejected")
            .OrderBy(item => item.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(2, deniedBillingAudits.Count);
        Assert.All(deniedBillingAudits, audit =>
        {
            Assert.Equal(tenantId, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal("billing-other-department", audit.ActorSubject);
            Assert.Equal("Rejected", audit.Outcome);
            Assert.Equal((int)HttpStatusCode.NotFound, audit.StatusCode);
        });
        Assert.Contains(deniedBillingAudits, audit => audit.Action == "GetBillingInvoiceForVisit");
        Assert.Contains(deniedBillingAudits, audit => audit.Action == "ListBillingInvoicePayments");
    }

    [Fact]
    public async Task CrossTenantRegistrarCannotReadInvoicesPaymentsOrReceiptsAndDenialsAreAudited()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (hospitalA, _, queueEntryA, _) = await SeedVisitAsync(tenantA);
        var (hospitalB, _, _, _) = await SeedVisitAsync(tenantB);

        await _factory.SeedTenantMembershipAsync(tenantA, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalA)]);
        await _factory.SeedTenantMembershipAsync(tenantA, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalA)],
            subject: "billing-tenant-a-cashier");
        await _factory.SeedTenantMembershipAsync(tenantB, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalB)],
            subject: "billing-tenant-b-cashier");

        using var admin = CreateClient(tenantA, TestJwtTokenFactory.Subject);
        using var cashierA = CreateClient(tenantA, "billing-tenant-a-cashier");
        using var cashierB = CreateClient(tenantB, "billing-tenant-b-cashier");

        var serviceResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}/billing/service-items",
            new BillingServiceItemInput("CONSULT", "OPD Consultation", "Consultation", 500m));
        Assert.Equal(HttpStatusCode.Created, serviceResponse.StatusCode);
        var service = await serviceResponse.Content.ReadFromJsonAsync<BillingServiceItemDto>();
        Assert.NotNull(service);

        var invoiceResponse = await cashierA.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}/billing/invoices",
            new InvoiceCreateInput(queueEntryA, [new InvoiceLineInput(service!.Id, 1)]));
        Assert.Equal(HttpStatusCode.Created, invoiceResponse.StatusCode);
        var invoice = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        Assert.NotNull(invoice);

        var paymentResponse = await cashierA.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}/billing/invoices/{invoice!.Id:D}/payments",
            new PaymentCreateInput(100m, PaymentMethods.Upi, "TENANT-A-ONLY-REF"));
        Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);
        var payment = await paymentResponse.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(payment);

        var billingBase = $"/api/v1/hospitals/{hospitalA:D}/billing";
        var attempts = new[]
        {
            await cashierB.GetAsync($"{billingBase}/invoices/{invoice.Id:D}"),
            await cashierB.GetAsync($"{billingBase}/invoices/{invoice.Id:D}/payments"),
            await cashierB.GetAsync($"{billingBase}/payments/{payment!.Id:D}/receipt"),
            await cashierB.GetAsync($"{billingBase}/visits/{queueEntryA:D}/invoice"),
            await cashierB.GetAsync($"{billingBase}/service-items"),
            await cashierB.GetAsync($"{billingBase}/reports/daily-collection?businessDate=2026-11-25&timeZoneId=Asia%2FKolkata")
        };

        Assert.All(attempts, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var responseBodies = await Task.WhenAll(attempts.Select(response => response.Content.ReadAsStringAsync()));
        Assert.All(responseBodies, body =>
        {
            Assert.DoesNotContain(invoice.InvoiceNumber, body, StringComparison.Ordinal);
            Assert.DoesNotContain("TENANT-A-ONLY-REF", body, StringComparison.Ordinal);
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var auditEvents = await db.AuditEvents
            .Where(audit => audit.ActorSubject == "billing-tenant-b-cashier")
            .OrderBy(audit => audit.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(6, auditEvents.Count);
        Assert.All(auditEvents, audit =>
        {
            Assert.Equal(tenantB, audit.TenantId);
            Assert.Equal(tenantB, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal("billing-tenant-b-cashier", audit.ActorSubject);
            Assert.Equal("Billing", audit.ResourceType);
            Assert.Equal("Rejected", audit.Outcome);
            Assert.Equal((int)HttpStatusCode.NotFound, audit.StatusCode);
        });

        Assert.Contains(auditEvents, audit =>
            audit.Action == "GetBillingInvoice" && audit.ResourceId == invoice.Id);
        Assert.Contains(auditEvents, audit =>
            audit.Action == "ListBillingInvoicePayments" && audit.ResourceId == invoice.Id);
        Assert.Contains(auditEvents, audit =>
            audit.Action == "GetBillingReceipt" && audit.ResourceId == payment!.Id);
        Assert.Contains(auditEvents, audit =>
            audit.Action == "GetBillingInvoiceForVisit" && audit.ResourceId == queueEntryA);
        Assert.Contains(auditEvents, audit =>
            audit.Action == "ListBillingServiceItems" && audit.ResourceId == hospitalA);
        Assert.Contains(auditEvents, audit =>
            audit.Action == "GetDailyBillingCollection" && audit.ResourceId == hospitalA);
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
