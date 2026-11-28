using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HeapCare.Api.Middleware;
using HeapCare.Application.Inpatient;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class InpatientBedEndpointAuditTests(FacilityApiFactory factory)
{
    [Fact]
    public async Task DeniedBedAssignmentAndTransfer_AreAuditedWithTrustedActorAndAdmissionResource()
    {
        factory.ResetDatabase();

        var tenantId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();
        var admissionId = Guid.NewGuid();
        var subject = $"ipd-bed-audit-{Guid.NewGuid():N}";
        var assignCorrelationId = $"ipd-assign-denied-{Guid.NewGuid():N}";
        var transferCorrelationId = $"ipd-transfer-denied-{Guid.NewGuid():N}";

        await factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.FacilityViewer,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Tenant)],
            subject: subject);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateToken(subject: subject));
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));

        using var assignRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions/{admissionId:D}/bed-placement")
        {
            Content = JsonContent.Create(new InpatientBedLocation(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid()))
        };
        assignRequest.Headers.Add(CorrelationIdMiddleware.HeaderName, assignCorrelationId);

        using var transferRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions/{admissionId:D}/bed-transfer")
        {
            Content = JsonContent.Create(new InpatientBedTransferInput(
                new InpatientBedLocation(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid()),
                "Synthetic denied-transfer audit test"))
        };
        transferRequest.Headers.Add(CorrelationIdMiddleware.HeaderName, transferCorrelationId);

        var assignResponse = await client.SendAsync(assignRequest);
        var transferResponse = await client.SendAsync(transferRequest);

        Assert.Equal(HttpStatusCode.Forbidden, assignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, transferResponse.StatusCode);

        using var verificationScope = factory.Services.CreateScope();
        verificationScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = verificationScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        var auditEvents = await db.AuditEvents
            .AsNoTracking()
            .Where(item => item.CorrelationId == assignCorrelationId ||
                           item.CorrelationId == transferCorrelationId)
            .ToListAsync();

        Assert.Equal(2, auditEvents.Count);

        var assignAudit = Assert.Single(
            auditEvents,
            item => item.CorrelationId == assignCorrelationId);
        Assert.Equal(tenantId, assignAudit.TenantId);
        Assert.Equal(tenantId, assignAudit.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, assignAudit.ActorIssuer);
        Assert.Equal(subject, assignAudit.ActorSubject);
        Assert.Equal("AssignInpatientBed", assignAudit.Action);
        Assert.Equal("IPD admissions", assignAudit.ResourceType);
        Assert.Equal(admissionId, assignAudit.ResourceId);
        Assert.Equal("POST", assignAudit.HttpMethod);
        Assert.Equal(
            "/api/v1/hospitals/{hospitalId:guid}/ipd/admissions/{admissionId:guid}/bed-placement",
            assignAudit.RouteTemplate);
        Assert.Equal("Denied", assignAudit.Outcome);
        Assert.Equal((int)HttpStatusCode.Forbidden, assignAudit.StatusCode);

        var transferAudit = Assert.Single(
            auditEvents,
            item => item.CorrelationId == transferCorrelationId);
        Assert.Equal(tenantId, transferAudit.TenantId);
        Assert.Equal(tenantId, transferAudit.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, transferAudit.ActorIssuer);
        Assert.Equal(subject, transferAudit.ActorSubject);
        Assert.Equal("TransferInpatientBed", transferAudit.Action);
        Assert.Equal("IPD admissions", transferAudit.ResourceType);
        Assert.Equal(admissionId, transferAudit.ResourceId);
        Assert.Equal("POST", transferAudit.HttpMethod);
        Assert.Equal(
            "/api/v1/hospitals/{hospitalId:guid}/ipd/admissions/{admissionId:guid}/bed-transfer",
            transferAudit.RouteTemplate);
        Assert.Equal("Denied", transferAudit.Outcome);
        Assert.Equal((int)HttpStatusCode.Forbidden, transferAudit.StatusCode);

        Assert.Empty(await db.InpatientBedPlacements.ToListAsync());
    }
}
