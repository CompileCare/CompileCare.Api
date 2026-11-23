using System.Net;
using System.Net.Http.Headers;
using CompileCare.Api.Middleware;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class AuditEventTests
{
    private readonly FacilityApiFactory _factory;

    public AuditEventTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task SuccessfulFacilityRequest_WritesTrustedTenantActorAndRouteMetadata()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin);

        using var client = CreateAuthenticatedClient(tenantId);
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, "audit-success-123");

        var response = await client.GetAsync("/api/v1/hospitals?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audit-success-123", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var auditEvent = await db.AuditEvents.SingleAsync();

        Assert.Equal(tenantId, auditEvent.TenantId);
        Assert.Equal(tenantId, auditEvent.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, auditEvent.ActorIssuer);
        Assert.Equal(TestJwtTokenFactory.Subject, auditEvent.ActorSubject);
        Assert.Equal("ListHospitals", auditEvent.Action);
        Assert.Equal("Hospitals", auditEvent.ResourceType);
        Assert.Null(auditEvent.ResourceId);
        Assert.Equal("GET", auditEvent.HttpMethod);
        Assert.Equal("/api/v1/hospitals", auditEvent.RouteTemplate);
        Assert.Equal("Succeeded", auditEvent.Outcome);
        Assert.Equal((int)HttpStatusCode.OK, auditEvent.StatusCode);
        Assert.Equal("audit-success-123", auditEvent.CorrelationId);
        Assert.NotEqual(default, auditEvent.OccurredAtUtc);
    }

    [Fact]
    public async Task TenantMembershipDenial_IsAuditedWithoutTrustingRequestedTenant()
    {
        var requestedTenantId = Guid.NewGuid();
        using var client = CreateAuthenticatedClient(requestedTenantId);

        var response = await client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var auditEvent = await db.AuditEvents.SingleAsync();

        Assert.Null(auditEvent.TenantId);
        Assert.Equal(requestedTenantId, auditEvent.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, auditEvent.ActorIssuer);
        Assert.Equal(TestJwtTokenFactory.Subject, auditEvent.ActorSubject);
        Assert.Equal("ListHospitals", auditEvent.Action);
        Assert.Equal("Denied", auditEvent.Outcome);
        Assert.Equal((int)HttpStatusCode.Forbidden, auditEvent.StatusCode);
    }

    [Fact]
    public async Task AuditEvents_AreAppendOnlyInEfCoreAndPostgreSql()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.HospitalAdmin);

        using var client = CreateAuthenticatedClient(tenantId);
        var response = await client.GetAsync("/api/v1/hospitals");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var auditEvent = await db.AuditEvents.SingleAsync();

        auditEvent.Action = "TamperedAction";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE audit_events SET \"Action\" = {"TamperedAction"} WHERE \"Id\" = {auditEvent.Id}"));
    }

    private HttpClient CreateAuthenticatedClient(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                TestJwtTokenFactory.CreateToken());
        client.DefaultRequestHeaders.Add(
            TenantContextMiddleware.HeaderName,
            tenantId.ToString("D"));
        return client;
    }
}
