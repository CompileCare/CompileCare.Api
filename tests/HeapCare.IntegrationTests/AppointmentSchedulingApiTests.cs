using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HeapCare.Api.Middleware;
using HeapCare.Application.Appointments;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HeapCare.IntegrationTests;

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
    public async Task AppointmentListIsPaginatedAndScopedToHospitalDepartmentAndLocalDate()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, providerId, patientId) = await SeedSchedulingDataAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, departmentId)],
            subject: "appointment-list-registrar");
        using var admin = CreateClient(tenantId, TestJwtTokenFactory.Subject);
        using var registrar = CreateClient(tenantId, "appointment-list-registrar");
        var date = new DateOnly(2026, 12, 12);
        var sessionResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions",
            new AvailabilitySessionCreateInput(departmentId, providerId, date,
                new TimeOnly(9, 0), new TimeOnly(10, 0), 30, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<AvailabilitySessionDto>();
        Assert.NotNull(session);
        var slots = await registrar.GetFromJsonAsync<List<AppointmentSlotDto>>(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions/{session!.Id:D}/slots");
        Assert.NotNull(slots);
        foreach (var slot in slots!)
        {
            var booking = await registrar.PostAsJsonAsync(
                $"/api/v1/hospitals/{hospitalId:D}/appointments",
                new AppointmentBookInput(session.Id, patientId, slot.StartsAtUtc, "Consultation"));
            Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        }

        var firstPage = await registrar.GetFromJsonAsync<AppointmentPage>(
            $"/api/v1/hospitals/{hospitalId:D}/appointments?departmentId={departmentId:D}&date={date:yyyy-MM-dd}&page=1&pageSize=1");
        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage!.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Single(firstPage.Items);

        var bookedOnly = await registrar.GetFromJsonAsync<AppointmentPage>(
            $"/api/v1/hospitals/{hospitalId:D}/appointments?departmentId={departmentId:D}&date={date:yyyy-MM-dd}&status=Booked");
        Assert.NotNull(bookedOnly);
        Assert.Equal(2, bookedOnly!.TotalCount);
        Assert.All(bookedOnly.Items, item => Assert.Equal(AppointmentStatuses.Booked, item.Status));

        var wrongDepartment = await registrar.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments?departmentId={Guid.NewGuid():D}&date={date:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.NotFound, wrongDepartment.StatusCode);

        var wrongDate = await registrar.GetFromJsonAsync<AppointmentPage>(
            $"/api/v1/hospitals/{hospitalId:D}/appointments?departmentId={departmentId:D}&date={date.AddDays(1):yyyy-MM-dd}");
        Assert.NotNull(wrongDate);
        Assert.Empty(wrongDate!.Items);
    }

    [Theory]
    [InlineData(TenantRoles.FacilityManager)]
    [InlineData(TenantRoles.FacilityViewer)]
    [InlineData(TenantRoles.ClinicalUser)]
    public async Task RolesWithoutSchedulingGrants_CannotReadAvailabilityOrBookAppointments(
        string role)
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, providerId, patientId) = await SeedSchedulingDataAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "scheduling-role-admin");
        await _factory.SeedTenantMembershipAsync(
            tenantId, role,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "scheduling-role-restricted");

        using var admin = CreateClient(tenantId, "scheduling-role-admin");
        using var restricted = CreateClient(tenantId, "scheduling-role-restricted");

        var date = new DateOnly(2026, 12, 20);
        var createSession = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions",
            new AvailabilitySessionCreateInput(
                departmentId, providerId, date,
                new TimeOnly(9, 0), new TimeOnly(10, 0), 30, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, createSession.StatusCode);
        var session = await createSession.Content.ReadFromJsonAsync<AvailabilitySessionDto>();
        Assert.NotNull(session);

        var slots = await admin.GetFromJsonAsync<List<AppointmentSlotDto>>(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions/{session!.Id:D}/slots");
        Assert.NotNull(slots);
        Assert.NotEmpty(slots!);
        var slot = slots![0];

        var availabilityRead = await restricted.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions?date={date:yyyy-MM-dd}&departmentId={departmentId:D}");
        var slotRead = await restricted.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions/{session.Id:D}/slots");
        var booking = await restricted.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments",
            new AppointmentBookInput(session.Id, patientId, slot.StartsAtUtc, "Consultation"));

        Assert.Equal(HttpStatusCode.Forbidden, availabilityRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, slotRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, booking.StatusCode);

        var appointments = await admin.GetFromJsonAsync<AppointmentPage>(
            $"/api/v1/hospitals/{hospitalId:D}/appointments?departmentId={departmentId:D}&date={date:yyyy-MM-dd}");
        Assert.NotNull(appointments);
        Assert.Equal(0, appointments!.TotalCount);
        Assert.Empty(appointments.Items);

        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var deniedAudits = await auditDb.AuditEvents.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId &&
                item.ActorSubject == "scheduling-role-restricted" &&
                item.ResourceType == "Appointments and scheduling" &&
                item.Outcome == "Denied")
            .OrderBy(item => item.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(3, deniedAudits.Count);
        Assert.All(deniedAudits, audit =>
        {
            Assert.Equal(tenantId, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal("scheduling-role-restricted", audit.ActorSubject);
            Assert.Equal((int)HttpStatusCode.Forbidden, audit.StatusCode);
        });
        Assert.Contains(deniedAudits, audit => audit.Action == "ListProviderAvailabilitySessions");
        Assert.Contains(deniedAudits, audit => audit.Action == "GetProviderAvailabilitySlots");
        Assert.Contains(deniedAudits, audit => audit.Action == "BookAppointment");
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

        var sessions = await registrar.GetFromJsonAsync<AvailabilitySessionPage>(
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
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            registrar.PostAsJsonAsync($"/api/v1/hospitals/{hospitalId:D}/appointments", request)));
        var outcomes = await Task.WhenAll(attempts.Select(async response => new
        {
            response.StatusCode,
            Body = await response.Content.ReadAsStringAsync()
        }));
        Assert.True(
            outcomes.Count(response => response.StatusCode == HttpStatusCode.Created) == 1,
            $"Expected one successful booking. Actual: {string.Join("; ", outcomes.Select(x => $"{(int)x.StatusCode} {x.Body}"))}");
        Assert.True(
            outcomes.Count(response => response.StatusCode == HttpStatusCode.Conflict) == 7,
            $"All competing requests must return 409. Actual: {string.Join("; ", outcomes.Select(x => $"{(int)x.StatusCode} {x.Body}"))}");
        Assert.All(outcomes, response =>
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
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
    public async Task ConcurrentAppointmentBookingAllowsOnlyOneWinnerAcrossRepeatedWaves()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, providerId, firstPatientId) = await SeedSchedulingDataAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "appointment-race-stress-registrar");

        // Use separate registered patients so each wave exercises only the
        // competing-booking race for one slot, not patient-overlap validation.
        var patientIds = new List<Guid> { firstPatientId };
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
            var additionalPatients = new[]
            {
                new Patient { GivenName = "Meera", FamilyName = "Kulkarni" },
                new Patient { GivenName = "Nikhil", FamilyName = "Joshi" }
            };
            db.Patients.AddRange(additionalPatients);
            db.HospitalPatientRegistrations.AddRange(additionalPatients.Select(patient =>
                new HospitalPatientRegistration
                {
                    PatientId = patient.Id,
                    HospitalId = hospitalId,
                    RegisteredAtUtc = DateTimeOffset.UtcNow
                }));
            await db.SaveChangesAsync();
            patientIds.AddRange(additionalPatients.Select(patient => patient.Id));
        }

        using var admin = CreateClient(tenantId, TestJwtTokenFactory.Subject);
        using var registrar = CreateClient(tenantId, "appointment-race-stress-registrar");
        var date = new DateOnly(2026, 12, 14);
        var sessionResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions",
            new AvailabilitySessionCreateInput(
                departmentId, providerId, date,
                new TimeOnly(9, 0), new TimeOnly(12, 0), 30, "Asia/Kolkata"));

        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<AvailabilitySessionDto>();
        Assert.NotNull(session);

        var slots = await registrar.GetFromJsonAsync<List<AppointmentSlotDto>>(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions/{session!.Id:D}/slots");
        Assert.NotNull(slots);
        Assert.Equal(6, slots!.Count);
        Assert.All(slots, slot => Assert.True(slot.IsAvailable));

        var bookedAppointments = new List<AppointmentDto>();
        for (var wave = 0; wave < 3; wave++)
        {
            var slot = slots[wave];
            var patientId = patientIds[wave];
            var request = new AppointmentBookInput(
                session.Id, patientId, slot.StartsAtUtc, $"Repeated race wave {wave + 1}");

            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                registrar.PostAsJsonAsync(
                    $"/api/v1/hospitals/{hospitalId:D}/appointments",
                    request)));
            var outcomes = await Task.WhenAll(responses.Select(async response => new
            {
                response.StatusCode,
                Body = await response.Content.ReadAsStringAsync()
            }));

            var createdResponses = responses
                .Where(response => response.StatusCode == HttpStatusCode.Created)
                .ToArray();
            Assert.True(
                createdResponses.Length == 1,
                $"Wave {wave + 1}: expected exactly one successful booking. Actual: {string.Join("; ", outcomes.Select(x => $"{(int)x.StatusCode} {x.Body}"))}");
            Assert.True(
                outcomes.Count(response => response.StatusCode == HttpStatusCode.Conflict) == 7,
                $"Wave {wave + 1}: expected seven conflicts. Actual: {string.Join("; ", outcomes.Select(x => $"{(int)x.StatusCode} {x.Body}"))}");
            Assert.All(outcomes, response =>
                Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));

            var appointment = await createdResponses[0].Content.ReadFromJsonAsync<AppointmentDto>();
            Assert.NotNull(appointment);
            Assert.Equal(AppointmentStatuses.Booked, appointment!.Status);
            Assert.Equal(patientId, appointment.PatientId);
            Assert.Equal(slot.StartsAtUtc, appointment.SlotStartsAtUtc);
            bookedAppointments.Add(appointment);
        }

        var appointmentPage = await registrar.GetFromJsonAsync<AppointmentPage>(
            $"/api/v1/hospitals/{hospitalId:D}/appointments?departmentId={departmentId:D}&date={date:yyyy-MM-dd}&page=1&pageSize=10");
        Assert.NotNull(appointmentPage);
        Assert.Equal(3, appointmentPage!.TotalCount);
        Assert.Equal(3, appointmentPage.Items.Select(item => item.SlotStartsAtUtc).Distinct().Count());
        Assert.Equal(
            patientIds.Order().ToArray(),
            appointmentPage.Items.Select(item => item.PatientId).Order().ToArray());
        Assert.All(appointmentPage.Items, item => Assert.Equal(AppointmentStatuses.Booked, item.Status));
        Assert.Equal(
            bookedAppointments.Select(item => item.Id).Order().ToArray(),
            appointmentPage.Items.Select(item => item.Id).Order().ToArray());
    }

    [Fact]
    public async Task ReschedulingPersistsTenantScopedAppointmentLineage()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, providerId, patientId) = await SeedSchedulingDataAsync(tenantId);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: "reschedule-reception-user");

        using var admin = CreateClient(tenantId, TestJwtTokenFactory.Subject);
        using var registrar = CreateClient(tenantId, "reschedule-reception-user");
        var date = new DateOnly(2026, 12, 11);
        var sessionResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions",
            new AvailabilitySessionCreateInput(
                departmentId, providerId, date, new TimeOnly(9, 0), new TimeOnly(10, 0), 30, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<AvailabilitySessionDto>();
        Assert.NotNull(session);

        var slots = await registrar.GetFromJsonAsync<List<AppointmentSlotDto>>(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions/{session!.Id:D}/slots");
        Assert.NotNull(slots);
        Assert.Equal(2, slots!.Count);

        var bookingResponse = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments",
            new AppointmentBookInput(session.Id, patientId, slots[0].StartsAtUtc, "Initial booking"));
        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        var original = await bookingResponse.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(original);

        var rescheduleResponse = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments/{original!.Id:D}/reschedule",
            new AppointmentBookInput(session.Id, patientId, slots[1].StartsAtUtc, "Rescheduled"));
        Assert.True(rescheduleResponse.StatusCode == HttpStatusCode.OK,
            $"Reschedule failed: {(int)rescheduleResponse.StatusCode} {await rescheduleResponse.Content.ReadAsStringAsync()}");
        var replacement = await rescheduleResponse.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(replacement);
        Assert.Equal(original.Id, replacement!.RescheduledFromAppointmentId);
        Assert.Equal(AppointmentStatuses.Booked, replacement.Status);

        var originalResponse = await registrar.GetAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments/{original.Id:D}");
        Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);
        var cancelled = await originalResponse.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(cancelled);
        Assert.Equal(AppointmentStatuses.Cancelled, cancelled!.Status);
    }

    [Fact]
    public async Task AppointmentCancellationIsAuditedAndPreventsFurtherLifecycleActions()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, providerId, patientId) = await SeedSchedulingDataAsync(tenantId);
        const string registrarSubject = "appointment-cancel-registrar";
        const string clinicalSubject = "appointment-cancel-clinical";

        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: registrarSubject);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.ClinicalUser,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: clinicalSubject);

        using var admin = CreateClient(tenantId, TestJwtTokenFactory.Subject);
        using var registrar = CreateClient(tenantId, registrarSubject);
        using var clinicalUser = CreateClient(tenantId, clinicalSubject);

        var date = new DateOnly(2026, 12, 18);
        var sessionResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions",
            new AvailabilitySessionCreateInput(
                departmentId, providerId, date,
                new TimeOnly(9, 0), new TimeOnly(10, 0), 30, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<AvailabilitySessionDto>();
        Assert.NotNull(session);

        var slots = await registrar.GetFromJsonAsync<List<AppointmentSlotDto>>(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions/{session!.Id:D}/slots");
        Assert.NotNull(slots);
        Assert.Equal(2, slots!.Count);

        var bookingResponse = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments",
            new AppointmentBookInput(session.Id, patientId, slots[0].StartsAtUtc, "Cancellation lifecycle"));
        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        var appointment = await bookingResponse.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(appointment);

        var deniedCancellation = await clinicalUser.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments/{appointment!.Id:D}/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedCancellation.StatusCode);

        var cancellation = await registrar.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments/{appointment.Id:D}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancellation.StatusCode);
        var cancelledAppointment = await cancellation.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(cancelledAppointment);
        Assert.Equal(AppointmentStatuses.Cancelled, cancelledAppointment!.Status);
        Assert.Equal(appointment.Id, cancelledAppointment.Id);

        var duplicateCancellation = await registrar.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments/{appointment.Id:D}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, duplicateCancellation.StatusCode);

        var rescheduleCancelled = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments/{appointment.Id:D}/reschedule",
            new AppointmentBookInput(session.Id, patientId, slots[1].StartsAtUtc, "Must not revive cancelled booking"));
        Assert.Equal(HttpStatusCode.Conflict, rescheduleCancelled.StatusCode);

        var checkInCancelled = await registrar.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments/{appointment.Id:D}/check-in", null);
        Assert.Equal(HttpStatusCode.Conflict, checkInCancelled.StatusCode);

        var readBack = await registrar.GetFromJsonAsync<AppointmentDto>(
            $"/api/v1/hospitals/{hospitalId:D}/appointments/{appointment.Id:D}");
        Assert.NotNull(readBack);
        Assert.Equal(AppointmentStatuses.Cancelled, readBack!.Status);

        var cancelledPage = await registrar.GetFromJsonAsync<AppointmentPage>(
            $"/api/v1/hospitals/{hospitalId:D}/appointments?departmentId={departmentId:D}&date={date:yyyy-MM-dd}&status=Cancelled");
        Assert.NotNull(cancelledPage);
        Assert.Contains(cancelledPage!.Items, item => item.Id == appointment.Id);
        Assert.DoesNotContain(cancelledPage.Items, item => item.Status != AppointmentStatuses.Cancelled);

        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var cancellationAudits = await auditDb.AuditEvents.AsNoTracking()
            .Where(audit =>
                audit.TenantId == tenantId &&
                audit.ResourceType == "Appointments and scheduling" &&
                audit.ResourceId == appointment.Id &&
                audit.Action == "CancelAppointment")
            .OrderBy(audit => audit.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(3, cancellationAudits.Count);
        var successfulCancellation = Assert.Single(
            cancellationAudits.Where(audit => audit.ActorSubject == registrarSubject && audit.Outcome == "Succeeded"));
        Assert.Equal((int)HttpStatusCode.OK, successfulCancellation.StatusCode);
        Assert.Equal(TestJwtTokenFactory.Issuer, successfulCancellation.ActorIssuer);
        Assert.Equal(tenantId, successfulCancellation.RequestedTenantId);

        var rejectedRepeat = Assert.Single(
            cancellationAudits.Where(audit => audit.ActorSubject == registrarSubject && audit.Outcome == "Rejected"));
        Assert.Equal((int)HttpStatusCode.Conflict, rejectedRepeat.StatusCode);
        Assert.Equal(TestJwtTokenFactory.Issuer, rejectedRepeat.ActorIssuer);

        var deniedByRole = Assert.Single(
            cancellationAudits.Where(audit => audit.ActorSubject == clinicalSubject && audit.Outcome == "Rejected"));
        Assert.Equal((int)HttpStatusCode.Forbidden, deniedByRole.StatusCode);
        Assert.Equal(TestJwtTokenFactory.Issuer, deniedByRole.ActorIssuer);

        // A second appointment has already checked in. Appointment cancellation
        // must not orphan its durable queue entry or change the appointment state.
        var checkedInBookingResponse = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments",
            new AppointmentBookInput(session.Id, patientId, slots[1].StartsAtUtc, "Already checked in"));
        Assert.Equal(HttpStatusCode.Created, checkedInBookingResponse.StatusCode);
        var checkedInAppointment = await checkedInBookingResponse.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(checkedInAppointment);

        var checkInResponse = await registrar.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments/{checkedInAppointment!.Id:D}/check-in", null);
        Assert.Equal(HttpStatusCode.OK, checkInResponse.StatusCode);

        var cancelAfterCheckIn = await registrar.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments/{checkedInAppointment.Id:D}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, cancelAfterCheckIn.StatusCode);

        var checkedInReadBack = await registrar.GetFromJsonAsync<AppointmentDto>(
            $"/api/v1/hospitals/{hospitalId:D}/appointments/{checkedInAppointment.Id:D}");
        Assert.NotNull(checkedInReadBack);
        Assert.Equal(AppointmentStatuses.Booked, checkedInReadBack!.Status);

        var checkedInQueueEntries = await auditDb.QueueEntries.AsNoTracking()
            .Where(entry => entry.AppointmentId == checkedInAppointment.Id)
            .ToListAsync();
        Assert.Single(checkedInQueueEntries);

        var deniedPostCheckInCancellation = await auditDb.AuditEvents.AsNoTracking()
            .Where(audit =>
                audit.TenantId == tenantId &&
                audit.ResourceType == "Appointments and scheduling" &&
                audit.ResourceId == checkedInAppointment.Id &&
                audit.Action == "CancelAppointment" &&
                audit.Outcome == "Rejected")
            .ToListAsync();
        var deniedCheckedInCancel = Assert.Single(deniedPostCheckInCancellation);
        Assert.Equal((int)HttpStatusCode.Conflict, deniedCheckedInCancel.StatusCode);
        Assert.Equal(registrarSubject, deniedCheckedInCancel.ActorSubject);
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

    [Fact]
    public async Task WalkInsReceiveSequentialTokensAndQueueTransitionsAreEnforced()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, _, patientId) = await SeedSchedulingDataAsync(tenantId);
        const string registrarSubject = "queue-reception-user";
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: registrarSubject);
        using var registrar = CreateClient(tenantId, registrarSubject);

        var request = new WalkInCreateInput(departmentId, patientId, "Asia/Kolkata");
        var responses = await Task.WhenAll(
            registrar.PostAsJsonAsync($"/api/v1/hospitals/{hospitalId:D}/queue/walk-ins", request),
            registrar.PostAsJsonAsync($"/api/v1/hospitals/{hospitalId:D}/queue/walk-ins", request));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var entries = await Task.WhenAll(responses.Select(
            response => response.Content.ReadFromJsonAsync<QueueEntryDto>()));
        Assert.All(entries, entry => Assert.NotNull(entry));
        Assert.Equal(new[] { 1, 2 }, entries.Select(entry => entry!.TokenNumber).Order().ToArray());

        var first = entries.Single(entry => entry!.TokenNumber == 1)!;
        var call = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{first.Id:D}/call", new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        var start = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{first.Id:D}/start", new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var complete = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{first.Id:D}/complete", new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var invalidRecall = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/{first.Id:D}/recall", new QueueActionInput(null));
        Assert.Equal(HttpStatusCode.Conflict, invalidRecall.StatusCode);
    }

    [Fact]
    public async Task AppointmentCheckInCreatesOneQueueEntryAndIsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentId, providerId, patientId) = await SeedSchedulingDataAsync(tenantId);
        const string registrarSubject = "appointment-checkin-user";
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: registrarSubject);
        using var registrar = CreateClient(tenantId, registrarSubject);

        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var localStart = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime.AddMinutes(10);
        var start = new TimeOnly(localStart.Hour, localStart.Minute, localStart.Second);
        var date = DateOnly.FromDateTime(localStart);
        var sessionResponse = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions",
            new AvailabilitySessionCreateInput(departmentId, providerId, date, start, start.AddMinutes(5), 5, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Forbidden, sessionResponse.StatusCode);

        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        using var admin = CreateClient(tenantId, TestJwtTokenFactory.Subject);
        sessionResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions",
            new AvailabilitySessionCreateInput(departmentId, providerId, date, start, start.AddMinutes(5), 5, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<AvailabilitySessionDto>();
        Assert.NotNull(session);

        var booking = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments",
            new AppointmentBookInput(session!.Id, patientId, session.StartsAtUtc, null));
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        var appointment = await booking.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(appointment);

        var checkInPath = $"/api/v1/hospitals/{hospitalId:D}/appointments/{appointment!.Id:D}/check-in";
        // Simultaneous arrivals for the same appointment must serialize at the
        // database boundary. Every caller should receive the one durable queue
        // entry rather than allocating a second token or returning a conflict.
        var concurrentResponses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => registrar.PostAsync(checkInPath, null)));
        Assert.All(concurrentResponses, response =>
            Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var returnedEntries = await Task.WhenAll(
            concurrentResponses.Select(response => response.Content.ReadFromJsonAsync<QueueEntryDto>()));
        Assert.All(returnedEntries, entry => Assert.NotNull(entry));
        var canonicalEntry = returnedEntries[0]!;
        Assert.All(returnedEntries, entry =>
        {
            Assert.Equal(canonicalEntry.Id, entry!.Id);
            Assert.Equal(canonicalEntry.TokenNumber, entry.TokenNumber);
            Assert.Equal(QueueEntryStatuses.Waiting, entry!.Status);
            Assert.Equal(canonicalEntry.Status, entry.Status);
        });

        using var verificationScope = _factory.Services.CreateScope();
        verificationScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var persistedEntries = await verificationDb.QueueEntries.AsNoTracking()
            .Where(entry => entry.AppointmentId == appointment.Id)
            .ToListAsync();
        var persistedEntry = Assert.Single(persistedEntries);
        Assert.Equal(canonicalEntry.Id, persistedEntry.Id);
        Assert.Equal(canonicalEntry.TokenNumber, persistedEntry.TokenNumber);
    }

    [Fact]
    public async Task CrossTenantHospitalAdminCannotReadBookOrMutateAppointmentsAndQueueOperationsAreAudited()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (hospitalA, departmentA, providerA, patientA) = await SeedSchedulingDataAsync(tenantA);
        var (hospitalB, _, _, _) = await SeedSchedulingDataAsync(tenantB);

        const string registrarA = "cross-tenant-appointment-registrar-a";
        const string adminB = "cross-tenant-appointment-admin-b";

        await _factory.SeedTenantMembershipAsync(
            tenantA, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalA)]);
        await _factory.SeedTenantMembershipAsync(
            tenantA, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalA)],
            subject: registrarA);
        await _factory.SeedTenantMembershipAsync(
            tenantB, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalB)],
            subject: adminB);

        using var hospitalAdminA = CreateClient(tenantA, TestJwtTokenFactory.Subject);
        using var registrarAClient = CreateClient(tenantA, registrarA);
        using var hospitalAdminB = CreateClient(tenantB, adminB);

        var date = new DateOnly(2026, 12, 10);
        var sessionResponse = await hospitalAdminA.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}/availability-sessions",
            new AvailabilitySessionCreateInput(
                departmentA, providerA, date,
                new TimeOnly(9, 0), new TimeOnly(10, 0), 30, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<AvailabilitySessionDto>();
        Assert.NotNull(session);

        var slots = await registrarAClient.GetFromJsonAsync<List<AppointmentSlotDto>>(
            $"/api/v1/hospitals/{hospitalA:D}/availability-sessions/{session!.Id:D}/slots");
        Assert.NotNull(slots);
        Assert.Equal(2, slots!.Count);

        var bookingResponse = await registrarAClient.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalA:D}/appointments",
            new AppointmentBookInput(session.Id, patientA, slots[0].StartsAtUtc, "Tenant A appointment"));
        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        var appointment = await bookingResponse.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(appointment);

        var attempts = new[]
        {
            await hospitalAdminB.PostAsJsonAsync(
                $"/api/v1/hospitals/{hospitalA:D}/availability-sessions",
                new AvailabilitySessionCreateInput(
                    departmentA, providerA, date.AddDays(1),
                    new TimeOnly(9, 0), new TimeOnly(10, 0), 30, "Asia/Kolkata")),
            await hospitalAdminB.GetAsync(
                $"/api/v1/hospitals/{hospitalA:D}/availability-sessions?date={date:yyyy-MM-dd}&page=1&pageSize=10"),
            await hospitalAdminB.GetAsync(
                $"/api/v1/hospitals/{hospitalA:D}/availability-sessions/{session.Id:D}/slots"),
            await hospitalAdminB.PostAsJsonAsync(
                $"/api/v1/hospitals/{hospitalA:D}/appointments",
                new AppointmentBookInput(session.Id, patientA, slots[1].StartsAtUtc, "Tenant B attempt")),
            await hospitalAdminB.GetAsync(
                $"/api/v1/hospitals/{hospitalA:D}/appointments/{appointment!.Id:D}"),
            await hospitalAdminB.PostAsync(
                $"/api/v1/hospitals/{hospitalA:D}/appointments/{appointment.Id:D}/cancel", null),
            await hospitalAdminB.PostAsJsonAsync(
                $"/api/v1/hospitals/{hospitalA:D}/appointments/{appointment.Id:D}/reschedule",
                new AppointmentBookInput(session.Id, patientA, slots[1].StartsAtUtc, "Tenant B attempt")),
            await hospitalAdminB.PostAsync(
                $"/api/v1/hospitals/{hospitalA:D}/appointments/{appointment.Id:D}/check-in", null),
            await hospitalAdminB.PostAsJsonAsync(
                $"/api/v1/hospitals/{hospitalA:D}/queue/walk-ins",
                new WalkInCreateInput(departmentA, patientA, "Asia/Kolkata"))
        };

        Assert.All(attempts, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantB);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var auditEvents = await db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorSubject == adminB)
            .OrderBy(audit => audit.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(attempts.Length, auditEvents.Count);
        Assert.All(auditEvents, audit =>
        {
            Assert.Equal(tenantB, audit.TenantId);
            Assert.Equal(tenantB, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal(adminB, audit.ActorSubject);
            Assert.Equal((int)HttpStatusCode.NotFound, audit.StatusCode);
            Assert.Equal("Rejected", audit.Outcome);
        });

        Assert.Contains(auditEvents, audit => audit.ResourceId == hospitalA);
        Assert.Contains(auditEvents, audit => audit.ResourceId == session.Id);
        Assert.Contains(auditEvents, audit => audit.ResourceId == appointment.Id);
    }

    [Fact]
    public async Task DepartmentScopedRegistrarCannotReadBookOrMutateAppointmentsAndQueueOperationsAreAudited()
    {
        var tenantId = Guid.NewGuid();
        var (hospitalId, departmentA, providerId, patientId) = await SeedSchedulingDataAsync(tenantId);
        Guid departmentB;
        using (var seedScope = _factory.Services.CreateScope())
        {
            seedScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = seedScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
            var department = new Department
            {
                HospitalId = hospitalId,
                Name = "Neurology",
                Code = "NEUR"
            };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
            departmentB = department.Id;
        }

        const string hospitalRegistrarSubject = "department-appointment-registrar-a";
        const string otherDepartmentSubject = "department-appointment-registrar-b";
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.HospitalAdmin,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)],
            subject: hospitalRegistrarSubject);
        await _factory.SeedTenantMembershipAsync(
            tenantId, TenantRoles.PatientRegistrar,
            scopes: [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, departmentB)],
            subject: otherDepartmentSubject);

        using var admin = CreateClient(tenantId, TestJwtTokenFactory.Subject);
        using var registrarA = CreateClient(tenantId, hospitalRegistrarSubject);
        using var registrarB = CreateClient(tenantId, otherDepartmentSubject);

        var date = new DateOnly(2026, 12, 10);
        var sessionResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions",
            new AvailabilitySessionCreateInput(
                departmentA, providerId, date,
                new TimeOnly(9, 0), new TimeOnly(10, 0), 30, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<AvailabilitySessionDto>();
        Assert.NotNull(session);

        var slots = await registrarA.GetFromJsonAsync<List<AppointmentSlotDto>>(
            $"/api/v1/hospitals/{hospitalId:D}/availability-sessions/{session!.Id:D}/slots");
        Assert.NotNull(slots);
        Assert.Equal(2, slots!.Count);

        var bookingResponse = await registrarA.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/appointments",
            new AppointmentBookInput(session.Id, patientId, slots[0].StartsAtUtc, "Department A appointment"));
        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        var appointment = await bookingResponse.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(appointment);

        var walkInResponse = await registrarA.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/queue/walk-ins",
            new WalkInCreateInput(departmentA, patientId, "Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Created, walkInResponse.StatusCode);
        var queueEntry = await walkInResponse.Content.ReadFromJsonAsync<QueueEntryDto>();
        Assert.NotNull(queueEntry);

        var attempts = new[]
        {
            await registrarB.GetAsync(
                $"/api/v1/hospitals/{hospitalId:D}/availability-sessions?date={date:yyyy-MM-dd}&departmentId={departmentA:D}&page=1&pageSize=10"),
            await registrarB.GetAsync(
                $"/api/v1/hospitals/{hospitalId:D}/availability-sessions/{session.Id:D}/slots"),
            await registrarB.PostAsJsonAsync(
                $"/api/v1/hospitals/{hospitalId:D}/appointments",
                new AppointmentBookInput(session.Id, patientId, slots[1].StartsAtUtc, "Department B attempt")),
            await registrarB.GetAsync(
                $"/api/v1/hospitals/{hospitalId:D}/appointments/{appointment!.Id:D}"),
            await registrarB.PostAsync(
                $"/api/v1/hospitals/{hospitalId:D}/appointments/{appointment.Id:D}/cancel", null),
            await registrarB.PostAsJsonAsync(
                $"/api/v1/hospitals/{hospitalId:D}/appointments/{appointment.Id:D}/reschedule",
                new AppointmentBookInput(session.Id, patientId, slots[1].StartsAtUtc, "Department B attempt")),
            await registrarB.PostAsync(
                $"/api/v1/hospitals/{hospitalId:D}/appointments/{appointment.Id:D}/check-in", null),
            await registrarB.GetAsync(
                $"/api/v1/hospitals/{hospitalId:D}/queue?departmentId={departmentA:D}&date={date:yyyy-MM-dd}&page=1&pageSize=10"),
            await registrarB.PostAsJsonAsync(
                $"/api/v1/hospitals/{hospitalId:D}/queue/{queueEntry!.Id:D}/call",
                new QueueActionInput(null)),
            await registrarB.PostAsJsonAsync(
                $"/api/v1/hospitals/{hospitalId:D}/queue/walk-ins",
                new WalkInCreateInput(departmentA, patientId, "Asia/Kolkata"))
        };

        Assert.All(attempts, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));

        using var auditScope = _factory.Services.CreateScope();
        auditScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var auditDb = auditScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var deniedAudits = await auditDb.AuditEvents.AsNoTracking()
            .Where(audit =>
                audit.TenantId == tenantId &&
                audit.ActorSubject == otherDepartmentSubject &&
                audit.ResourceType == "Appointments and scheduling" &&
                audit.Outcome == "Rejected")
            .OrderBy(audit => audit.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(attempts.Length, deniedAudits.Count);
        Assert.All(deniedAudits, audit =>
        {
            Assert.Equal(tenantId, audit.RequestedTenantId);
            Assert.Equal(TestJwtTokenFactory.Issuer, audit.ActorIssuer);
            Assert.Equal(otherDepartmentSubject, audit.ActorSubject);
            Assert.Equal((int)HttpStatusCode.NotFound, audit.StatusCode);
        });
        Assert.Contains(deniedAudits, audit => audit.Action == "ListProviderAvailabilitySessions");
        Assert.Contains(deniedAudits, audit => audit.Action == "GetProviderAvailabilitySlots");
        Assert.Contains(deniedAudits, audit => audit.Action == "BookAppointment");
        Assert.Contains(deniedAudits, audit => audit.Action == "GetAppointment");
        Assert.Contains(deniedAudits, audit => audit.Action == "CancelAppointment");
        Assert.Contains(deniedAudits, audit => audit.Action == "RescheduleAppointment");
        Assert.Contains(deniedAudits, audit => audit.Action == "CheckInAppointment");
        Assert.Contains(deniedAudits, audit => audit.Action == "ListDepartmentQueue");
        Assert.Contains(deniedAudits, audit => audit.Action == "TransitionQueueEntry");
        Assert.Contains(deniedAudits, audit => audit.Action == "RegisterQueueWalkIn");
        Assert.Contains(deniedAudits, audit => audit.ResourceId == hospitalId);
        Assert.Contains(deniedAudits, audit => audit.ResourceId == session.Id);
        Assert.Contains(deniedAudits, audit => audit.ResourceId == appointment.Id);
        Assert.Contains(deniedAudits, audit => audit.ResourceId == queueEntry.Id);
    }

    private async Task<(Guid HospitalId, Guid DepartmentId, Guid ProviderId, Guid PatientId)> SeedSchedulingDataAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
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
