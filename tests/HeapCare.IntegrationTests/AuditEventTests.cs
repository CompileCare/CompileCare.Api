using System.Net;
using System.Net.Http.Headers;
using HeapCare.Api.Middleware;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HeapCare.IntegrationTests;

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
    public void ExplicitAuditResourceId_OverridesParentHospitalRouteIdentifier()
    {
        var hospitalId = Guid.NewGuid();
        var admissionId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        var endpoint = new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/hospitals/{hospitalId:guid}/ipd/admissions"),
            0,
            EndpointMetadataCollection.Empty,
            "CreateInpatientAdmission");

        context.SetEndpoint(endpoint);
        context.Request.RouteValues["hospitalId"] = hospitalId;
        context.Items[AuditEventMiddleware.ResourceIdItemKey] = admissionId;

        Assert.Equal(admissionId, AuditEventMiddleware.ParseRouteResourceId(context));
        Assert.NotEqual(hospitalId, AuditEventMiddleware.ParseRouteResourceId(context));
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
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var auditEvent = await db.AuditEvents.SingleAsync();

        Assert.Equal(tenantId, auditEvent.TenantId);
        Assert.Equal(tenantId, auditEvent.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, auditEvent.ActorIssuer);
        Assert.Equal(TestJwtTokenFactory.Subject, auditEvent.ActorSubject);
        Assert.Equal("ListHospitals", auditEvent.Action);
        Assert.Equal("Hospitals", auditEvent.ResourceType);
        Assert.Null(auditEvent.ResourceId);
        Assert.Equal("GET", auditEvent.HttpMethod);
        Assert.StartsWith("/api/v1/hospitals", auditEvent.RouteTemplate, StringComparison.Ordinal);
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
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
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
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var auditEvent = await db.AuditEvents.SingleAsync();

        auditEvent.Action = "TamperedAction";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();

        var tamperedAction = "TamperedAction";
        await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE audit_events SET \"Action\" = {tamperedAction} WHERE \"Id\" = {auditEvent.Id}"));

        await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE audit_events"));
    }


    [Fact]
    public async Task AnonymousTenantScopedRequest_IsAuditedAsDeniedWithoutInventingActorOrTenant()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var auditEvent = await db.AuditEvents.SingleAsync();

        Assert.Null(auditEvent.TenantId);
        Assert.Null(auditEvent.RequestedTenantId);
        Assert.Null(auditEvent.ActorIssuer);
        Assert.Null(auditEvent.ActorSubject);
        Assert.Equal("ListHospitals", auditEvent.Action);
        Assert.Equal("Denied", auditEvent.Outcome);
        Assert.Equal((int)HttpStatusCode.Unauthorized, auditEvent.StatusCode);
    }

    [Fact]
    public async Task InvalidTenantContext_IsAuditedAsRejectedWithValidatedActorOnly()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken());
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, "not-a-tenant-guid");

        var response = await client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var auditEvent = await db.AuditEvents.SingleAsync();

        Assert.Null(auditEvent.TenantId);
        Assert.Null(auditEvent.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, auditEvent.ActorIssuer);
        Assert.Equal(TestJwtTokenFactory.Subject, auditEvent.ActorSubject);
        Assert.Equal("ListHospitals", auditEvent.Action);
        Assert.Equal("Rejected", auditEvent.Outcome);
        Assert.Equal((int)HttpStatusCode.BadRequest, auditEvent.StatusCode);
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
