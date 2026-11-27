using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HeapCare.Api.Middleware;
using HeapCare.Application.Patients;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class AuditFailureRollbackTests
{
    private readonly FacilityApiFactory _factory;

    public AuditFailureRollbackTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task PatientRegistration_WhenAuditInsertFails_ReturnsServiceUnavailableAndRollsBackBusinessWrite()
    {
        var tenantId = Guid.NewGuid();
        await _factory.SeedTenantMembershipAsync(tenantId, TenantRoles.PatientRegistrar);
        var hospitalId = await SeedHospitalAsync(tenantId);

        // Simulate an unavailable audit sink at the database boundary. The trigger
        // fails only when the middleware attempts to persist the audit event.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_audit_event_insert() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'synthetic audit sink failure';
                END;
                $$ LANGUAGE plpgsql;
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER reject_audit_event_insert_trigger
                BEFORE INSERT ON audit_events
                FOR EACH ROW EXECUTE FUNCTION reject_audit_event_insert();
                """);
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken());
        client.DefaultRequestHeaders.Add(
            TenantContextMiddleware.HeaderName,
            tenantId.ToString("D"));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients",
            new PatientRegistrationInput(
                "Synthetic",
                null,
                "Rollback",
                new DateOnly(1990, 4, 12)));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var verificationScope = _factory.Services.CreateScope();
        verificationScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        Assert.False(await verificationDb.Patients.AnyAsync(patient =>
            patient.GivenName == "Synthetic" && patient.FamilyName == "Rollback"));
        Assert.False(await verificationDb.HospitalPatientRegistrations.AnyAsync(registration =>
            registration.HospitalId == hospitalId));
        Assert.Empty(await verificationDb.AuditEvents.ToListAsync());
    }

    private async Task<Guid> SeedHospitalAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var hospital = new Hospital
        {
            Name = "Audit rollback test hospital",
            Code = Guid.NewGuid().ToString("N")[..8]
        };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();
        return hospital.Id;
    }
}
