using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HeapCare.Api.Middleware;
using HeapCare.Application.Auditing;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class AuditQueryApiTests
{
    private readonly FacilityApiFactory _factory;

    public AuditQueryApiTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task TenantWideHospitalAdminCanQueryAndExportAuditMetadata()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin);
        using var client = CreateAuthenticatedClient(tenantId);

        var sourceResponse = await client.GetAsync("/api/v1/hospitals?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, sourceResponse.StatusCode);

        var fromUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        var toUtc = DateTimeOffset.UtcNow.AddMinutes(1);
        var query = BuildQuery(fromUtc, toUtc);
        var response = await client.GetAsync($"/api/v1/audit/events?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<AuditEventPage>();
        Assert.NotNull(page);
        Assert.Contains(page!.Items, item => item.Action == "ListHospitals");
        Assert.All(page.Items, item => Assert.Equal(tenantId, item.TenantId));

        var export = await client.GetAsync($"/api/v1/audit/events/export?{query}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        Assert.Contains("heapcare-audit-events.csv", export.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);
        var csv = await export.Content.ReadAsStringAsync();
        Assert.Contains("ActorIssuer,ActorSubject,Action,ResourceType", csv, StringComparison.Ordinal);
        Assert.Contains("ListHospitals", csv, StringComparison.Ordinal);

        // The export request itself must also be captured by the audit middleware.
        var afterExport = await client.GetAsync($"/api/v1/audit/events?{query}");
        Assert.Equal(HttpStatusCode.OK, afterExport.StatusCode);
        var afterExportPage = await afterExport.Content.ReadFromJsonAsync<AuditEventPage>();
        Assert.NotNull(afterExportPage);
        Assert.Contains(afterExportPage!.Items, item => item.Action == "ExportAuditEvents");
    }

    [Fact]
    public async Task InvalidHttpBindingsReturnClientErrorsInsteadOfInternalServerErrors()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin);
        using var client = CreateAuthenticatedClient(tenantId);

        var missingQueryParameter = await client.GetAsync(
            $"/api/v1/audit/events?toUtc={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}");
        Assert.Equal(HttpStatusCode.BadRequest, missingQueryParameter.StatusCode);

        using var emptyBodyRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/hospitals/{Guid.NewGuid():D}/queue/{Guid.NewGuid():D}/call")
        {
            Content = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json")
        };

        var emptyBodyResponse = await client.SendAsync(emptyBodyRequest);
        Assert.Equal(HttpStatusCode.BadRequest, emptyBodyResponse.StatusCode);
    }

    [Fact]
    public async Task HospitalScopedAdministratorCannotReadTenantWideAuditEvents()
    {
        var tenantId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin, subject: "audit-tenant-seed");
        await SeedHospitalAsync(tenantId, hospitalId);
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: TestJwtTokenFactory.Subject);
        using var client = CreateAuthenticatedClient(tenantId);

        var fromUtc = DateTimeOffset.UtcNow.AddDays(-1);
        var toUtc = DateTimeOffset.UtcNow;
        var query = BuildQuery(fromUtc, toUtc);
        var response = await client.GetAsync($"/api/v1/audit/events?{query}");
        var export = await client.GetAsync($"/api/v1/audit/events/export?{query}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
    }

    [Fact]
    public async Task DepartmentScopedAdministratorCannotReadOrExportTenantWideAuditEvents()
    {
        var tenantId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin, subject: "audit-department-seed");
        await SeedHospitalAsync(tenantId, hospitalId);
        var departmentId = await SeedDepartmentAsync(tenantId, hospitalId);
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, departmentId)],
            subject: TestJwtTokenFactory.Subject);
        using var client = CreateAuthenticatedClient(tenantId);

        var fromUtc = DateTimeOffset.UtcNow.AddDays(-1);
        var toUtc = DateTimeOffset.UtcNow;
        var query = BuildQuery(fromUtc, toUtc);
        var response = await client.GetAsync($"/api/v1/audit/events?{query}");
        var export = await client.GetAsync($"/api/v1/audit/events/export?{query}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
    }

    [Theory]
    [InlineData(TenantRoles.PatientRegistrar)]
    [InlineData(TenantRoles.ClinicalUser)]
    [InlineData(TenantRoles.FacilityManager)]
    [InlineData(TenantRoles.FacilityViewer)]
    public async Task MembershipWithoutAuditPermissionCannotQueryOrExportEvenWithForgedTokenClaims(string role)
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, role);
        using var client = CreateAuthenticatedClient(
            tenantId,
            additionalClaims:
            [
                new("compilecare:permission", "audit.read"),
                new("compilecare:tenant_role", TenantRoles.HospitalAdmin),
                new("role", TenantRoles.HospitalAdmin),
                new(System.Security.Claims.ClaimTypes.Role, TenantRoles.HospitalAdmin)
            ]);

        var query = BuildQuery(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow);
        var listResponse = await client.GetAsync($"/api/v1/audit/events?{query}");
        var exportResponse = await client.GetAsync($"/api/v1/audit/events/export?{query}");

        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, exportResponse.StatusCode);
    }

    [Fact]
    public async Task AuditQueryAndExportEnforceMaximumTimeRanges()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin);
        using var client = CreateAuthenticatedClient(tenantId);

        var tooWideQuery = BuildQuery(DateTimeOffset.UtcNow.AddDays(-91), DateTimeOffset.UtcNow);
        var queryResponse = await client.GetAsync($"/api/v1/audit/events?{tooWideQuery}");
        Assert.Equal(HttpStatusCode.BadRequest, queryResponse.StatusCode);

        var tooWideExport = BuildQuery(DateTimeOffset.UtcNow.AddDays(-32), DateTimeOffset.UtcNow);
        var exportResponse = await client.GetAsync($"/api/v1/audit/events/export?{tooWideExport}");
        Assert.Equal(HttpStatusCode.BadRequest, exportResponse.StatusCode);
    }

    [Fact]
    public async Task AuditQueryRejectsNonUtcOffsetsAndFutureEndTimes()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin);
        using var client = CreateAuthenticatedClient(tenantId);

        var localFrom = new DateTimeOffset(
            DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-2), DateTimeKind.Unspecified),
            TimeSpan.FromHours(5.5));
        var localTo = localFrom.AddHours(1);
        var nonUtcQuery =
            $"fromUtc={Uri.EscapeDataString(localFrom.ToString("O"))}" +
            $"&toUtc={Uri.EscapeDataString(localTo.ToString("O"))}";
        var nonUtcResponse = await client.GetAsync($"/api/v1/audit/events?{nonUtcQuery}");

        var futureQuery = BuildQuery(
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(2));
        var futureResponse = await client.GetAsync($"/api/v1/audit/events?{futureQuery}");

        Assert.Equal(HttpStatusCode.BadRequest, nonUtcResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, futureResponse.StatusCode);
    }

    [Fact]
    public async Task AuditQueryCannotReturnEventsFromAnotherTenant()
    {
        var firstTenantId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(firstTenantId, TenantRoles.HospitalAdmin);
        await _factory.SeedTenantMembershipAsync(secondTenantId, TenantRoles.HospitalAdmin);

        using var firstClient = CreateAuthenticatedClient(firstTenantId);
        using var secondClient = CreateAuthenticatedClient(secondTenantId);
        Assert.Equal(HttpStatusCode.OK, (await firstClient.GetAsync("/api/v1/hospitals")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondClient.GetAsync("/api/v1/hospitals")).StatusCode);

        var fromUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        var toUtc = DateTimeOffset.UtcNow.AddMinutes(1);
        var response = await firstClient.GetAsync($"/api/v1/audit/events?{BuildQuery(fromUtc, toUtc)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<AuditEventPage>();
        Assert.NotNull(page);
        Assert.NotEmpty(page!.Items);
        Assert.All(page.Items, item => Assert.Equal(firstTenantId, item.TenantId));
        Assert.DoesNotContain(page.Items, item => item.TenantId == secondTenantId);
    }

    [Fact]
    public async Task CsvExportNeutralizesSpreadsheetFormulaValues()
    {
        const string subject = "\t=HYPERLINK(\"https://example.invalid\",\"open\")";
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin, subject: subject);
        using var client = CreateAuthenticatedClient(tenantId, subject);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/hospitals")).StatusCode);
        var query = BuildQuery(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
        var response = await client.GetAsync($"/api/v1/audit/events/export?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"'\t=HYPERLINK", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditQueryPaginationUsesStableIdTieBreakerForIdenticalTimestamps()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin);
        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var expectedIds = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Guid.Parse("00000000-0000-0000-0000-000000000001")
        };

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
            db.AuditEvents.AddRange(expectedIds.Select(id => new AuditEvent
            {
                Id = id,
                TenantId = tenantId,
                Action = "StablePagination",
                ResourceType = "Test",
                HttpMethod = "GET",
                RouteTemplate = "/test",
                Outcome = "Succeeded",
                StatusCode = (int)HttpStatusCode.OK,
                CorrelationId = Guid.NewGuid().ToString("N"),
                OccurredAtUtc = occurredAt
            }));
            await db.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient(tenantId);
        var query = BuildQuery(occurredAt.AddMinutes(-1), occurredAt.AddMinutes(1));
        var firstResponse = await client.GetAsync($"/api/v1/audit/events?{query}&page=1&pageSize=2");
        var secondResponse = await client.GetAsync($"/api/v1/audit/events?{query}&page=2&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var firstPage = await firstResponse.Content.ReadFromJsonAsync<AuditEventPage>();
        var secondPage = await secondResponse.Content.ReadFromJsonAsync<AuditEventPage>();
        Assert.NotNull(firstPage);
        Assert.NotNull(secondPage);
        Assert.Equal(3, firstPage!.TotalCount);
        Assert.Equal(expectedIds.Take(2), firstPage.Items.Select(item => item.Id));
        Assert.Equal(expectedIds.Skip(2), secondPage!.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task CsvExportNeutralizesAllSpreadsheetFormulaPrefixesAndLeadingWhitespace()
    {
        var tenantId = Guid.NewGuid();
        const string exporter = "audit-csv-exporter";
        var subjects = new[]
        {
            "=1+1",
            "+1+1",
            "-1+1",
            "@SUM(1,1)",
            " \t=1+1"
        };

        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin, subject: exporter);
        foreach (var subject in subjects)
        {
            await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin, subject: subject);
            using var actor = CreateAuthenticatedClient(tenantId, subject);
            Assert.Equal(HttpStatusCode.OK, (await actor.GetAsync("/api/v1/hospitals")).StatusCode);
        }

        using var exportClient = CreateAuthenticatedClient(tenantId, exporter);
        var query = BuildQuery(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
        var response = await exportClient.GetAsync($"/api/v1/audit/events/export?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"'=1+1\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"'+1+1\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"'-1+1\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"'@SUM(1,1)\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"' \t=1+1\"", csv, StringComparison.Ordinal);
    }

    private async Task SeedHospitalAsync(Guid tenantId, Guid hospitalId)
    {
        using var scope = _factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        db.Hospitals.Add(new Hospital
        {
            Id = hospitalId,
            TenantId = tenantId,
            Name = "Audit scope test hospital",
            Code = $"AUD-{hospitalId:N}"[..12]
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedDepartmentAsync(Guid tenantId, Guid hospitalId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var department = new Department
        {
            HospitalId = hospitalId,
            Name = "Audit scope department",
            Code = "AUDDEP"
        };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return department.Id;
    }

    private HttpClient CreateAuthenticatedClient(
        Guid tenantId,
        string? subject = null,
        IEnumerable<System.Security.Claims.Claim>? additionalClaims = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken(
                subject ?? TestJwtTokenFactory.Subject,
                additionalClaims: additionalClaims));
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
    }

    private static string BuildQuery(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        $"fromUtc={Uri.EscapeDataString(fromUtc.ToUniversalTime().ToString("O"))}" +
        $"&toUtc={Uri.EscapeDataString(toUtc.ToUniversalTime().ToString("O"))}";
}
