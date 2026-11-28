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
public sealed class InpatientAdmissionAuthorizationTests(FacilityApiFactory factory)
{
    [Theory]
    [InlineData(TenantRoles.HospitalAdmin)]
    [InlineData(TenantRoles.FacilityManager)]
    [InlineData(TenantRoles.FacilityViewer)]
    [InlineData(TenantRoles.PatientRegistrar)]
    [InlineData(TenantRoles.ClinicalUser)]
    public async Task ExistingTenantRoles_DoNotReceiveIPDAdmissionPermission(string role)
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();
        var subject = $"ipd-admit-denied-{role}";

        await factory.SeedTenantMembershipAsync(
            tenantId,
            role,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Tenant)],
            subject: subject);

        using var client = CreateClient(tenantId, subject);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions",
            new InpatientAdmissionInput(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlatformOperator_DoesNotReceiveTenantIPDAdmissionAccess()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();

        await factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.FacilityViewer,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Tenant)],
            subject: TestJwtTokenFactory.Subject);
        await factory.SeedPlatformOperatorAsync();

        using var client = CreateClient(tenantId, TestJwtTokenFactory.Subject);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions",
            new InpatientAdmissionInput(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ForgedExternalAdmissionPermission_CannotGrantAdmissionAccess()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();
        const string subject = "ipd-forged-admit-permission";

        await factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.FacilityViewer,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Tenant)],
            subject: subject);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateToken(
                subject: subject,
                additionalClaims:
                [
                    new System.Security.Claims.Claim("heapcare:permission", InpatientPermissions.Admit),
                    new System.Security.Claims.Claim("role", TenantRoles.HospitalAdmin)
                ]));
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions",
            new InpatientAdmissionInput(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeniedAdmissionCreation_IsRecordedAsMetadataOnlyAuditEvent()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();
        const string subject = "ipd-audit-denied-admitter";
        var correlationId = $"ipd-admission-denied-{Guid.NewGuid():N}";

        await factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.FacilityViewer,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Tenant)],
            subject: subject);

        using var client = CreateClient(tenantId, subject);
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, correlationId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions",
            new InpatientAdmissionInput(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var auditEvent = await db.AuditEvents.SingleAsync(
            item => item.CorrelationId == correlationId);

        Assert.Equal(tenantId, auditEvent.RequestedTenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, auditEvent.ActorIssuer);
        Assert.Equal(subject, auditEvent.ActorSubject);
        Assert.Equal("CreateInpatientAdmission", auditEvent.Action);
        Assert.Equal("IPD admissions", auditEvent.ResourceType);
        Assert.Equal(hospitalId, auditEvent.ResourceId);
        Assert.Equal("POST", auditEvent.HttpMethod);
        Assert.Equal(
            "/api/v1/hospitals/{hospitalId:guid}/ipd/admissions",
            auditEvent.RouteTemplate);
        Assert.Equal("Denied", auditEvent.Outcome);
        Assert.Equal((int)HttpStatusCode.Forbidden, auditEvent.StatusCode);
        Assert.Equal(correlationId, auditEvent.CorrelationId);
    }

    private HttpClient CreateClient(Guid tenantId, string subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateToken(subject: subject));
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
    }
}
