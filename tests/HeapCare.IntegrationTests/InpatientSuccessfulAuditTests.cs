using System.Security.Claims;
using HeapCare.Api.Middleware;
using HeapCare.Api.Security;
using HeapCare.Application.Inpatient;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class InpatientSuccessfulAuditTests(FacilityApiFactory factory)
{
    private const string Issuer = "https://identity.heapcare.test/";
    private const string Subject = "ipd-success-audit-user";

    [Fact]
    public async Task SuccessfulBedAssignment_CommitsPlacementAndMetadataOnlyAuditTogether()
    {
        factory.ResetDatabase();
        var data = await SeedAsync(bedCount: 2);

        using var scope = CreateRequestScope(data);
        var service = scope.ServiceProvider.GetRequiredService<IInpatientService>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        var response = await InvokeAuditedAsync(
            scope,
            tenant,
            data,
            "AssignInpatientBed",
            "/api/v1/hospitals/{hospitalId:guid}/ipd/admissions/{admissionId:guid}/bed-placement",
            async () =>
            {
                var result = await service.AssignBedAsync(
                    data.HospitalId,
                    data.AdmissionId,
                    data.Locations[0],
                    new InpatientActor(Issuer, Subject),
                    CancellationToken.None);
                Assert.True(result.IsSuccess, result.Error);
                return result.StatusCode;
            });

        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);

        var auditEvent = await scope.ServiceProvider.GetRequiredService<HeapCareDbContext>()
            .AuditEvents.SingleAsync();

        Assert.Equal(data.TenantId, auditEvent.TenantId);
        Assert.Equal(data.TenantId, auditEvent.RequestedTenantId);
        Assert.Equal(Issuer, auditEvent.ActorIssuer);
        Assert.Equal(Subject, auditEvent.ActorSubject);
        Assert.Equal("AssignInpatientBed", auditEvent.Action);
        Assert.Equal("IPD admissions", auditEvent.ResourceType);
        Assert.Equal(data.AdmissionId, auditEvent.ResourceId);
        Assert.Equal("POST", auditEvent.HttpMethod);
        Assert.Equal(
            "/api/v1/hospitals/{hospitalId:guid}/ipd/admissions/{admissionId:guid}/bed-placement",
            auditEvent.RouteTemplate);
        Assert.Equal("Succeeded", auditEvent.Outcome);
        Assert.Equal(StatusCodes.Status200OK, auditEvent.StatusCode);
        Assert.Equal("ipd-success-assignment", auditEvent.CorrelationId);

        var placement = await scope.ServiceProvider.GetRequiredService<HeapCareDbContext>()
            .InpatientBedPlacements.SingleAsync();
        Assert.Equal(data.AdmissionId, placement.AdmissionId);
        Assert.Equal(data.Locations[0].BedId, placement.BedId);
        Assert.Null(placement.EndedAtUtc);
    }

    [Fact]
    public async Task SuccessfulBedTransfer_CommitsPlacementHistoryAndMetadataOnlyAuditTogether()
    {
        factory.ResetDatabase();
        var data = await SeedAsync(bedCount: 2);

        using var scope = CreateRequestScope(data);
        var service = scope.ServiceProvider.GetRequiredService<IInpatientService>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        var initialAssignment = await service.AssignBedAsync(
            data.HospitalId,
            data.AdmissionId,
            data.Locations[0],
            new InpatientActor(Issuer, Subject),
            CancellationToken.None);
        Assert.True(initialAssignment.IsSuccess, initialAssignment.Error);

        var response = await InvokeAuditedAsync(
            scope,
            tenant,
            data,
            "TransferInpatientBed",
            "/api/v1/hospitals/{hospitalId:guid}/ipd/admissions/{admissionId:guid}/bed-transfer",
            async () =>
            {
                var result = await service.TransferBedAsync(
                    data.HospitalId,
                    data.AdmissionId,
                    new InpatientBedTransferInput(data.Locations[1], "Audit integration transfer"),
                    new InpatientActor(Issuer, Subject),
                    CancellationToken.None);
                Assert.True(result.IsSuccess, result.Error);
                return result.StatusCode;
            });

        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);

        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var auditEvent = await db.AuditEvents.SingleAsync();

        Assert.Equal(data.TenantId, auditEvent.TenantId);
        Assert.Equal(data.TenantId, auditEvent.RequestedTenantId);
        Assert.Equal(Issuer, auditEvent.ActorIssuer);
        Assert.Equal(Subject, auditEvent.ActorSubject);
        Assert.Equal("TransferInpatientBed", auditEvent.Action);
        Assert.Equal("IPD admissions", auditEvent.ResourceType);
        Assert.Equal(data.AdmissionId, auditEvent.ResourceId);
        Assert.Equal("POST", auditEvent.HttpMethod);
        Assert.Equal(
            "/api/v1/hospitals/{hospitalId:guid}/ipd/admissions/{admissionId:guid}/bed-transfer",
            auditEvent.RouteTemplate);
        Assert.Equal("Succeeded", auditEvent.Outcome);
        Assert.Equal(StatusCodes.Status200OK, auditEvent.StatusCode);
        Assert.Equal("ipd-success-transfer", auditEvent.CorrelationId);

        var placements = await db.InpatientBedPlacements
            .OrderBy(item => item.StartedAtUtc)
            .ToListAsync();
        Assert.Equal(2, placements.Count);
        Assert.NotNull(placements[0].EndedAtUtc);
        Assert.Equal("Audit integration transfer", placements[0].EndReason);
        Assert.Null(placements[1].EndedAtUtc);
        Assert.Equal(data.Locations[1].BedId, placements[1].BedId);
    }

    private static async Task<DefaultHttpContext> InvokeAuditedAsync(
        IServiceScope scope,
        ITenantContext tenant,
        TestData data,
        string endpointName,
        string routeTemplate,
        Func<Task<int>> operation)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = scope.ServiceProvider;
        context.Request.Method = "POST";
        context.Request.Headers[TenantContextMiddleware.HeaderName] = data.TenantId.ToString("D");
        context.TraceIdentifier = endpointName == "AssignInpatientBed"
            ? "ipd-success-assignment"
            : "ipd-success-transfer";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AuthorizationPolicies.ValidatedIssuerClaim, Issuer),
            new Claim("sub", Subject)
        ], authenticationType: "SyntheticAuthenticatedActor"));

        var endpoint = new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(routeTemplate),
            0,
            new EndpointMetadataCollection(
                TenantScopedEndpointMetadata.Instance,
                new TestTagsMetadata(["IPD admissions"]),
                new TestEndpointNameMetadata(endpointName)),
            endpointName);
        context.SetEndpoint(endpoint);
        context.Request.RouteValues["hospitalId"] = data.HospitalId.ToString("D");
        context.Request.RouteValues["admissionId"] = data.AdmissionId.ToString("D");

        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AuditEventMiddleware>>();
        var middleware = new AuditEventMiddleware(async httpContext =>
        {
            httpContext.Response.StatusCode = await operation();
        });

        await middleware.InvokeAsync(context, db, tenant, logger);
        return context;
    }

    private IServiceScope CreateRequestScope(TestData data)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(data.TenantId);
        scope.ServiceProvider.GetRequiredService<IOrganizationScopeContext>().SetMembership(
            Guid.NewGuid(),
            TenantRoles.HospitalAdmin,
            [new OrganizationScope(OrganizationScopeTypes.Hospital, data.HospitalId)]);
        return scope;
    }

    private async Task<TestData> SeedAsync(int bedCount)
    {
        var tenantId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "IPD successful audit test tenant",
            Slug = $"ipd-success-audit-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital
        {
            Name = "IPD successful audit hospital",
            Code = $"IPDSA-{Guid.NewGuid():N}"[..12]
        };
        var department = new Department
        {
            HospitalId = hospital.Id,
            Name = "IPD successful audit department",
            Code = "IPDAUDIT"
        };
        var ward = new Ward
        {
            DepartmentId = department.Id,
            Name = "IPD successful audit ward"
        };
        var room = new Room
        {
            WardId = ward.Id,
            Name = "IPD successful audit room"
        };
        var patient = new Patient
        {
            GivenName = "Synthetic",
            FamilyName = "Audit Patient"
        };

        db.Hospitals.Add(hospital);
        db.Departments.Add(department);
        db.Wards.Add(ward);
        db.Rooms.Add(room);
        db.Patients.Add(patient);
        db.HospitalPatientRegistrations.Add(new HospitalPatientRegistration
        {
            PatientId = patient.Id,
            HospitalId = hospital.Id,
            RegisteredAtUtc = DateTimeOffset.UtcNow
        });

        var locations = new List<InpatientBedLocation>();
        for (var index = 0; index < bedCount; index++)
        {
            var bed = new Bed
            {
                RoomId = room.Id,
                Number = $"AUDIT-{index + 1}",
                IsActive = true
            };
            db.Beds.Add(bed);
            locations.Add(new InpatientBedLocation(
                department.Id, ward.Id, room.Id, bed.Id));
        }

        var admission = new InpatientAdmission
        {
            HospitalId = hospital.Id,
            PatientId = patient.Id,
            Status = InpatientAdmissionStatuses.Active,
            AdmittedAtUtc = DateTimeOffset.UtcNow
        };
        db.InpatientAdmissions.Add(admission);
        await db.SaveChangesAsync();

        return new TestData(tenantId, hospital.Id, admission.Id, locations);
    }

    private sealed record TestData(
        Guid TenantId,
        Guid HospitalId,
        Guid AdmissionId,
        IReadOnlyList<InpatientBedLocation> Locations);

    private sealed record TestTagsMetadata(IReadOnlyList<string> Tags) : ITagsMetadata;

    private sealed record TestEndpointNameMetadata(string EndpointName) : IEndpointNameMetadata;
}
