using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Appointments;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class AppointmentSchedulingApiTests
{
    private readonly FacilityApiFactory _factory;

    public AppointmentSchedulingApiTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task HospitalAdminCanPublishAvailabilityAndRegistrarCanBookOnlyRegisteredPatient()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, providerId, patientId) = await SeedSchedulingDataAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        const string registrarSubject = "appointment-reception-user";
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: registrarSubject);

        using var admin = CreateClient(tenantId, TestJwtTokenFactory.Subject);
        using var registrar = CreateClient(tenantId, registrarSubject);

        var date = new DateOnly(2026, 12, 10);
        var createSession = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions",
            new AvailabilitySessionCreateInput(
                departmentId, providerId, date,
                new TimeOnly(9, 0), new TimeOnly(10, 0), 30, "Asia/Kolkata"));

        Assert.Equal(HttpStatusCode.Created, createSession.StatusCode);
        var session = await createSession.Content.ReadFromJsonAsync<AvailabilitySessionDto>();
        Assert.NotNull(session);
        Assert.Equal(date, session!.LocalDate);
        Assert.Equal(TimeSpan.FromHours(3.5), session.StartsAtUtc.TimeOfDay);

        var overlappingSession = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions",
            new AvailabilitySessionCreateInput(
                departmentId, providerId, date,
                new TimeOnly(9, 30), new TimeOnly(10, 30), 30, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Conflict, overlappingSession.StatusCode);

        var sessions = await registrar.GetFromJsonAsync<AppointmentPage>(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions?date={date:yyyy-MM-dd}&page=1&pageSize=10");
        Assert.NotNull(sessions);
        Assert.Single(sessions!.Items);

        var slots = await registrar.GetFromJsonAsync<List<AppointmentSlotDto>>(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions/{session.Id:D}/slots");
        Assert.NotNull(slots);
        Assert.Equal(2, slots!.Count);
        Assert.All(slots, slot => Assert.True(slot.IsAvailable));

        var unregistered = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments",
            new AppointmentBookInput(session.Id, Guid.NewGuid(), slots[0].StartsAtUtc, null));
        Assert.Equal(HttpStatusCode.Conflict, unregistered.StatusCode);

        var request = new AppointmentBookInput(session.Id, patientId, slots[0].StartsAtUtc, "Follow-up");
        var attempts = await Task.WhenAll(
            registrar.PostAsJsonAsync($"/api/v1/hospitals/{hospitalId:D}/appointments", request),
            registrar.PostAsJsonAsync($"/api/v1/hospitals/{hospitalId:D}/appointments", request));
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Conflict);
        var booked = attempts.Single(response => response.StatusCode == HttpStatusCode.Created);
        var appointment = await booked.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(appointment);
        Assert.Equal(AppointmentStatuses.Booked, appointment!.Status);
        Assert.Equal(patientId, appointment.PatientId);

        var cancel = await registrar.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments/{appointment.Id:D}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var rebook = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments", request);
        Assert.Equal(HttpStatusCode.Created, rebook.StatusCode);
    }

    [Fact]
    public async Task AvailabilityRejectsInvalidTimeZoneAndSlotDuration()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, providerId, _) = await SeedSchedulingDataAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        using var admin = CreateClient(tenantId, TestJwtTokenFactory.Subject);

        var invalidZone = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions",
            new AvailabilitySessionCreateInput(departmentId, providerId, new DateOnly(2026, 12, 10),
                new TimeOnly(9, 0), new TimeOnly(10, 0), 30, "Mars/Olympus"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidZone.StatusCode);

        var invalidDuration = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions",
            new AvailabilitySessionCreateInput(departmentId, providerId, new DateOnly(2026, 12, 10),
                new TimeOnly(9, 0), new TimeOnly(10, 0), 17, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidDuration.StatusCode);
    }

    private async Task<(Guid HospitalId, Guid DepartmentId, Guid ProviderId, Guid PatientId)> SeedSchedulingDataAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Scheduling test tenant",
            Slug = $"scheduling-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital { Name = "Scheduling Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();
        var department = new Department { HospitalId = hospital.Id, Name = "Medicine", Code = "MED" };
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        var provider = new StaffProfile
        {
            StaffCode = "PROVIDER-01",
            GivenName = "Asha",
            FamilyName = "Patil",
            ProviderDesignation = "Consultant"
        };
        db.StaffProfiles.Add(provider);
        db.StaffAssignments.Add(new StaffAssignment
        {
            StaffProfileId = provider.Id,
            HospitalId = hospital.Id,
            DepartmentId = department.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });

        var patient = new Patient { GivenName = "Ravi", FamilyName = "Deshmukh" };
        db.Patients.Add(patient);
        db.HospitalPatientRegistrations.Add(new HospitalPatientRegistration
        {
            PatientId = patient.Id,
            HospitalId = hospital.Id,
            RegisteredAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return (hospital.Id, department.Id, provider.Id, patient.Id);
    }

    private HttpClient CreateClient(Guid tenantId, string subject)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken(subject));
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
    }
}
