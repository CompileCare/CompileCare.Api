using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HeapCare.Api.Middleware;
using HeapCare.Application.Inpatient;
using HeapCare.Application.Tenancy;
using Xunit;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class InpatientEndpointAuthorizationTests(FacilityApiFactory factory)
{
    [Theory]
    [InlineData(TenantRoles.HospitalAdmin)]
    [InlineData(TenantRoles.FacilityManager)]
    [InlineData(TenantRoles.FacilityViewer)]
    [InlineData(TenantRoles.PatientRegistrar)]
    [InlineData(TenantRoles.ClinicalUser)]
    public async Task ExistingTenantRoles_DoNotReceiveIPDReadAssignOrTransferPermissions(string role)
    {
        var tenantId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();
        var admissionId = Guid.NewGuid();

        await factory.SeedTenantMembershipAsync(
            tenantId,
            role,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Tenant)],
            subject: $"ipd-policy-{role}");

        using var client = CreateClient(tenantId, $"ipd-policy-{role}");

        var read = await client.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions/{admissionId:D}");
        var assign = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions/{admissionId:D}/bed-placement",
            new InpatientBedLocation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        var transfer = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions/{admissionId:D}/bed-transfer",
            new InpatientBedTransferInput(
                new InpatientBedLocation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                "Authorization regression test"));

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, transfer.StatusCode);
    }

    [Fact]
    public async Task PlatformOperator_DoesNotImplicitlyReceiveTenantIPDAccess()
    {
        var tenantId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();
        var admissionId = Guid.NewGuid();
        await factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.FacilityViewer,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Tenant)],
            subject: TestJwtTokenFactory.Subject);
        await factory.SeedPlatformOperatorAsync();

        using var client = CreateClient(tenantId, TestJwtTokenFactory.Subject);

        var read = await client.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions/{admissionId:D}");
        var assign = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions/{admissionId:D}/bed-placement",
            new InpatientBedLocation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
    }

    [Fact]
    public async Task ForgedExternalPermissionClaims_CannotGrantIPDAccess()
    {
        var tenantId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();
        var admissionId = Guid.NewGuid();

        await factory.SeedTenantMembershipAsync(
            tenantId,
            TenantRoles.FacilityViewer,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Tenant)],
            subject: "ipd-forged-permission");

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateToken(
                subject: "ipd-forged-permission",
                additionalClaims:
                [
                    new System.Security.Claims.Claim("heapcare:permission", InpatientPermissions.Read),
                    new System.Security.Claims.Claim("heapcare:permission", InpatientPermissions.BedAssign),
                    new System.Security.Claims.Claim("heapcare:permission", InpatientPermissions.BedTransfer),
                    new System.Security.Claims.Claim("role", TenantRoles.HospitalAdmin)
                ]));
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));

        var read = await client.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions/{admissionId:D}");
        var assign = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/ipd/admissions/{admissionId:D}/bed-placement",
            new InpatientBedLocation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
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
