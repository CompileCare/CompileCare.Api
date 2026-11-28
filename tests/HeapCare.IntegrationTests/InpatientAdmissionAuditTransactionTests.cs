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
public sealed class InpatientAdmissionAuditTransactionTests(FacilityApiFactory factory)
{
    private const string Issuer = "https://identity.heapcare.test/";
    private const string Subject = "ipd-admission-audit-user";
    private const string RouteTemplate = "/api/v1/hospitals/{hospitalId:guid}/ipd/admissions";

    [Fact]
    public async Task SuccessfulAdmissionCreation_CommitsAdmissionAndMetadataOnlyAuditTogether()
    {
        factory.ResetDatabase();
        var data = await SeedAsync();

        using var scope = CreateRequestScope(data);
        var service = scope.ServiceProvider.GetRequiredService<IInpatientService>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        InpatientAdmissionDto? createdAdmission = null;
        var response = await InvokeAuditedAsync(scope, data, async context =>
        {
            var result = await service.CreateAdmissionAsync(
                data.HospitalId,
                new InpatientAdmissionInput(data.PatientId),
                new InpatientActor(Issuer, Subject),
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error);
            createdAdmission = result.Value;
            Assert.NotNull(createdAdmission);

            // Mirror the endpoint's resource override: the created Admission is
            // the audited resource, not the parent Hospital from the route.
            context.Items[AuditEventMiddleware.ResourceIdItemKey] = createdAdmission!.Id;
            context.Response.StatusCode = StatusCodes.Status201Created;
        });

        Assert.Equal(StatusCodes.Status201Created, response.Response.StatusCode);
        Assert.NotNull(createdAdmission);

        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var auditEvent = await db.AuditEvents.SingleAsync(
            item => item.CorrelationId == response.TraceIdentifier);

        Assert.Equal(data.TenantId, auditEvent.TenantId);
        Assert.Equal(data.TenantId, auditEvent.RequestedTenantId);
        Assert.Equal(Issuer, auditEvent.ActorIssuer);
        Assert.Equal(Subject, auditEvent.ActorSubject);
        Assert.Equal("CreateInpatientAdmission", auditEvent.Action);
        Assert.Equal("IPD admissions", auditEvent.ResourceType);
        Assert.Equal(createdAdmission!.Id, auditEvent.ResourceId);
        Assert.NotEqual(data.PatientId, auditEvent.ResourceId);
        Assert.Equal("POST", auditEvent.HttpMethod);
        Assert.Equal(RouteTemplate, auditEvent.RouteTemplate);
        Assert.Equal("Succeeded", auditEvent.Outcome);
        Assert.Equal(StatusCodes.Status201Created, auditEvent.StatusCode);
        Assert.Equal(response.TraceIdentifier, auditEvent.CorrelationId);

        var persistedAdmission = await db.InpatientAdmissions.SingleAsync(
            item => item.Id == createdAdmission.Id);
        Assert.Equal(data.PatientId, persistedAdmission.PatientId);
        Assert.Equal(data.HospitalId, persistedAdmission.HospitalId);
        Assert.Equal(InpatientAdmissionStatuses.Active, persistedAdmission.Status);
        Assert.Empty(await db.InpatientBedPlacements.ToListAsync());
    }

    [Fact]
    public async Task AuditInsertFailure_RollsBackAdmissionCreationAndReturnsServiceUnavailable()
    {
        factory.ResetDatabase();
        var data = await SeedAsync();

        using var scope = CreateRequestScope(data);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IInpatientService>();
        await InstallAuditFailureTriggerAsync(db);

        try
        {
            InpatientAdmissionDto? createdAdmission = null;
            var response = await InvokeAuditedAsync(scope, data, async context =>
            {
                var result = await service.CreateAdmissionAsync(
                    data.HospitalId,
                    new InpatientAdmissionInput(data.PatientId),
                    new InpatientActor(Issuer, Subject),
                    CancellationToken.None);

                Assert.True(result.IsSuccess, result.Error);
                createdAdmission = result.Value;
                Assert.NotNull(createdAdmission);
                context.Items[AuditEventMiddleware.ResourceIdItemKey] = createdAdmission!.Id;
                context.Response.StatusCode = StatusCodes.Status201Created;
            });

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.Response.StatusCode);

            db.ChangeTracker.Clear();
            var persistedAdmissions = await db.InpatientAdmissions
                .AsNoTracking()
                .Where(item => item.PatientId == data.PatientId &&
                               item.HospitalId == data.HospitalId)
                .ToListAsync();
            Assert.Empty(persistedAdmissions);
            Assert.Empty(await db.AuditEvents.AsNoTracking().ToListAsync());
            Assert.Empty(await db.InpatientBedPlacements.AsNoTracking().ToListAsync());
        }
        finally
        {
            await RemoveAuditFailureTriggerAsync(db);
        }
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

    private async Task<TestData> SeedAsync()
    {
        var tenantId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "IPD admission audit tenant",
            Slug = $"ipd-admission-audit-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital
        {
            Name = "IPD admission audit hospital",
            Code = $"IPDA-{Guid.NewGuid():N}"[..12]
        };
        var patient = new Patient
        {
            GivenName = "Synthetic",
            FamilyName = "Admission Audit"
        };

        db.Hospitals.Add(hospital);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        db.HospitalPatientRegistrations.Add(new HospitalPatientRegistration
        {
            PatientId = patient.Id,
            HospitalId = hospital.Id,
            RegisteredAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        return new TestData(tenantId, hospital.Id, patient.Id);
    }

    private static async Task<DefaultHttpContext> InvokeAuditedAsync(
        IServiceScope scope,
        TestData data,
        RequestDelegate endpoint)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        context.Request.Method = "POST";
        context.Request.Headers[TenantContextMiddleware.HeaderName] = data.TenantId.ToString("D");
        context.TraceIdentifier = $"ipd-admission-audit-{Guid.NewGuid():N}";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AuthorizationPolicies.ValidatedIssuerClaim, Issuer),
            new Claim("sub", Subject)
        ], authenticationType: "SyntheticAuthenticatedActor"));

        var routeEndpoint = new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(RouteTemplate),
            0,
            new EndpointMetadataCollection(
                TenantScopedEndpointMetadata.Instance,
                new TestTagsMetadata(["IPD admissions"]),
                new TestEndpointNameMetadata("CreateInpatientAdmission")),
            "CreateInpatientAdmission");
        context.SetEndpoint(routeEndpoint);
        context.Request.RouteValues["hospitalId"] = data.HospitalId.ToString("D");

        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AuditEventMiddleware>>();
        var middleware = new AuditEventMiddleware(endpoint);

        await middleware.InvokeAsync(context, db, tenant, logger);
        return context;
    }

    private static async Task InstallAuditFailureTriggerAsync(HeapCareDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION reject_ipd_admission_audit_insert() RETURNS trigger AS $ipd$
            BEGIN
                RAISE EXCEPTION 'synthetic IPD admission audit sink failure';
            END;
            $ipd$ LANGUAGE plpgsql;
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_ipd_admission_audit_insert_trigger
            BEFORE INSERT ON audit_events
            FOR EACH ROW EXECUTE FUNCTION reject_ipd_admission_audit_insert();
            """);
    }

    private static async Task RemoveAuditFailureTriggerAsync(HeapCareDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER IF EXISTS reject_ipd_admission_audit_insert_trigger ON audit_events;
            """);
        await db.Database.ExecuteSqlRawAsync("""
            DROP FUNCTION IF EXISTS reject_ipd_admission_audit_insert();
            """);
    }

    private sealed record TestData(Guid TenantId, Guid HospitalId, Guid PatientId);

    private sealed record TestTagsMetadata(IReadOnlyList<string> Tags) : ITagsMetadata;

    private sealed record TestEndpointNameMetadata(string EndpointName) : IEndpointNameMetadata;
}
