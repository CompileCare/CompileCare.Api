using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Auditing;
using CompileCare.Application.Tenancy;

namespace CompileCare.IntegrationTests;

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
        var csv = await export.Content.ReadAsStringAsync();
        Assert.Contains("ActorIssuer,ActorSubject,Action,ResourceType", csv, StringComparison.Ordinal);
        Assert.Contains("ListHospitals", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HospitalScopedAdministratorCannotReadTenantWideAuditEvents()
    {
        var tenantId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        using var client = CreateAuthenticatedClient(tenantId);

        var fromUtc = DateTimeOffset.UtcNow.AddDays(-1);
        var toUtc = DateTimeOffset.UtcNow;
        var response = await client.GetAsync($"/api/v1/audit/events?{BuildQuery(fromUtc, toUtc)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
        const string subject = "=HYPERLINK(\"https://example.invalid\",\"open\")";
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin, subject: subject);
        using var client = CreateAuthenticatedClient(tenantId, subject);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/hospitals")).StatusCode);
        var query = BuildQuery(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
        var response = await client.GetAsync($"/api/v1/audit/events/export?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"'=HYPERLINK", csv, StringComparison.Ordinal);
    }

    private HttpClient CreateAuthenticatedClient(Guid tenantId, string? subject = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken(subject));
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
    }

    private static string BuildQuery(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        $"fromUtc={Uri.EscapeDataString(fromUtc.ToUniversalTime().ToString("O"))}" +
        $"&toUtc={Uri.EscapeDataString(toUtc.ToUniversalTime().ToString("O"))}";
}
