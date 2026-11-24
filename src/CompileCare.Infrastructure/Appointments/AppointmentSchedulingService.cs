using CompileCare.Application.Appointments;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Appointments;

public sealed class AppointmentSchedulingService(
    CompileCareDbContext db,
    IOrganizationScopeContext organizationScope) : IAppointmentSchedulingService
{
    public async Task<AppointmentResult<AvailabilitySessionDto>> CreateAvailabilityAsync(
        Guid hospitalId, AvailabilitySessionCreateInput input, CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || input.DepartmentId == Guid.Empty ||
            input.ProviderStaffId == Guid.Empty || !organizationScope.CanManageDepartment(hospitalId, input.DepartmentId))
            return AppointmentResult<AvailabilitySessionDto>.Failure("Hospital or department not found.", 404);

        if (input.SlotDurationMinutes is < 5 or > 120)
            return AppointmentResult<AvailabilitySessionDto>.Failure("Slot duration must be between 5 and 120 minutes.", 400);
        if (string.IsNullOrWhiteSpace(input.TimeZoneId) || input.TimeZoneId.Length > 100)
            return AppointmentResult<AvailabilitySessionDto>.Failure("A valid hospital time zone is required.", 400);

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return AppointmentResult<AvailabilitySessionDto>.Failure("The supplied IANA time zone is not recognized.", 400);
        }
        catch (InvalidTimeZoneException)
        {
            return AppointmentResult<AvailabilitySessionDto>.Failure("The supplied time zone is invalid.", 400);
        }

        var localStart = DateTime.SpecifyKind(input.LocalDate.ToDateTime(input.StartLocalTime), DateTimeKind.Unspecified);
        var localEnd = DateTime.SpecifyKind(input.LocalDate.ToDateTime(input.EndLocalTime), DateTimeKind.Unspecified);
        if (localEnd <= localStart)
            return AppointmentResult<AvailabilitySessionDto>.Failure("Availability must start and end on the same local date, with end after start.", 400);
        if (timeZone.IsInvalidTime(localStart) || timeZone.IsInvalidTime(localEnd) ||
            timeZone.IsAmbiguousTime(localStart) || timeZone.IsAmbiguousTime(localEnd))
            return AppointmentResult<AvailabilitySessionDto>.Failure("The local availability time is ambiguous or does not exist in the selected time zone.", 400);

        var duration = (localEnd - localStart).TotalMinutes;
        if (duration > 12 * 60 || duration % input.SlotDurationMinutes != 0)
            return AppointmentResult<AvailabilitySessionDto>.Failure("The session must be at most 12 hours and evenly divisible by its slot duration.", 400);

        var startsAtUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone));
        var endsAtUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone));

        var departmentExists = await db.Departments.AnyAsync(
            x => x.Id == input.DepartmentId && x.HospitalId == hospitalId, cancellationToken);
        if (!departmentExists)
            return AppointmentResult<AvailabilitySessionDto>.Failure("Hospital or department not found.", 404);

        var providerAssigned = await (
            from staff in db.StaffProfiles
            join assignment in db.StaffAssignments on staff.Id equals assignment.StaffProfileId
            where staff.Id == input.ProviderStaffId && staff.IsActive &&
                  staff.ProviderDesignation != null && assignment.HospitalId == hospitalId &&
                  assignment.DepartmentId == input.DepartmentId && assignment.IsActive &&
                  assignment.EffectiveFrom <= input.LocalDate &&
                  (assignment.EffectiveTo == null || assignment.EffectiveTo >= input.LocalDate)
            select staff.Id).AnyAsync(cancellationToken);
        if (!providerAssigned)
            return AppointmentResult<AvailabilitySessionDto>.Failure(
                "The provider must be active and assigned to this hospital and department for the selected date.", 409);

        var overlaps = await db.ProviderAvailabilitySessions.AnyAsync(x =>
            x.ProviderStaffId == input.ProviderStaffId && x.LocalDate == input.LocalDate &&
            x.StartsAtUtc < endsAtUtc && x.EndsAtUtc > startsAtUtc, cancellationToken);
        if (overlaps)
            return AppointmentResult<AvailabilitySessionDto>.Failure("The provider already has an overlapping availability session.", 409);

        var session = new ProviderAvailabilitySession
        {
            HospitalId = hospitalId,
            DepartmentId = input.DepartmentId,
            ProviderStaffId = input.ProviderStaffId,
            LocalDate = input.LocalDate,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc,
            SlotDurationMinutes = input.SlotDurationMinutes,
            TimeZoneId = input.TimeZoneId
        };
        db.ProviderAvailabilitySessions.Add(session);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: "23P01" or "23505" })
        {
            return AppointmentResult<AvailabilitySessionDto>.Failure("The provider already has overlapping availability.", 409);
        }

        return AppointmentResult<AvailabilitySessionDto>.Success(ToDto(session));
    }

    public async Task<AppointmentResult<AppointmentPage>> ListAvailabilityAsync(
        Guid hospitalId, DateOnly localDate, Guid? departmentId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        if (!CanReadHospital(hospitalId, departmentId))
            return AppointmentResult<AppointmentPage>.Failure("Availability not found.", 404);

        page = page < 1 ? 1 : Math.Min(page, 2_000_000);
        pageSize = pageSize < 1 ? 25 : Math.Min(pageSize, 100);
        var query = db.ProviderAvailabilitySessions.Where(x =>
            x.HospitalId == hospitalId && x.LocalDate == localDate);
        if (departmentId.HasValue)
            query = query.Where(x => x.DepartmentId == departmentId.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.ProviderStaffId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AvailabilitySessionDto(
                x.Id, x.HospitalId, x.DepartmentId, x.ProviderStaffId, x.LocalDate,
                x.StartsAtUtc, x.EndsAtUtc, x.SlotDurationMinutes, x.TimeZoneId, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return AppointmentResult<AppointmentPage>.Success(new(items, page, pageSize, total));
    }

    public async Task<AppointmentResult<IReadOnlyList<AppointmentSlotDto>>> GetSlotsAsync(
        Guid hospitalId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || sessionId == Guid.Empty)
            return AppointmentResult<IReadOnlyList<AppointmentSlotDto>>.Failure("Availability session not found.", 404);
        var session = await db.ProviderAvailabilitySessions.FirstOrDefaultAsync(
            x => x.Id == sessionId && x.HospitalId == hospitalId, cancellationToken);
        if (session is null || !CanReadHospital(hospitalId, session.DepartmentId))
            return AppointmentResult<IReadOnlyList<AppointmentSlotDto>>.Failure("Availability session not found.", 404);

        var bookedStarts = await db.Appointments
            .Where(x => x.AvailabilitySessionId == sessionId && x.Status == AppointmentStatuses.Booked)
            .Select(x => x.SlotStartsAtUtc)
            .ToListAsync(cancellationToken);
        var booked = bookedStarts.ToHashSet();
        var slots = new List<AppointmentSlotDto>();
        for (var start = session.StartsAtUtc; start < session.EndsAtUtc;
             start = start.AddMinutes(session.SlotDurationMinutes))
        {
            var end = start.AddMinutes(session.SlotDurationMinutes);
            if (end > session.EndsAtUtc) break;
            slots.Add(new(start, end, !booked.Contains(start)));
        }
        return AppointmentResult<IReadOnlyList<AppointmentSlotDto>>.Success(slots);
    }

    public async Task<AppointmentResult<AppointmentDto>> BookAsync(
        Guid hospitalId, AppointmentBookInput input, CancellationToken cancellationToken)
    {
        if (input.AvailabilitySessionId == Guid.Empty || input.PatientId == Guid.Empty)
            return AppointmentResult<AppointmentDto>.Failure("A valid availability session and patient are required.", 400);

        var session = await db.ProviderAvailabilitySessions.FirstOrDefaultAsync(
            x => x.Id == input.AvailabilitySessionId && x.HospitalId == hospitalId, cancellationToken);
        if (session is null || !organizationScope.CanManageDepartment(hospitalId, session.DepartmentId))
            return AppointmentResult<AppointmentDto>.Failure("Availability session not found.", 404);

        if (input.Reason?.Length > 250)
            return AppointmentResult<AppointmentDto>.Failure("Appointment reason must be at most 250 characters.", 400);

        var slotStart = input.SlotStartsAtUtc.ToUniversalTime();
        var slotEnd = slotStart.AddMinutes(session.SlotDurationMinutes);
        var offset = (slotStart - session.StartsAtUtc).TotalMinutes;
        if (slotStart < session.StartsAtUtc || slotEnd > session.EndsAtUtc ||
            offset < 0 || offset % session.SlotDurationMinutes != 0)
            return AppointmentResult<AppointmentDto>.Failure("The selected start time is not a slot in this availability session.", 400);

        var registered = await db.HospitalPatientRegistrations.AnyAsync(
            x => x.HospitalId == hospitalId && x.PatientId == input.PatientId, cancellationToken);
        if (!registered)
            return AppointmentResult<AppointmentDto>.Failure(
                "The patient must have an active registration at this hospital before booking.", 409);

        var patientOverlap = await db.Appointments.AnyAsync(x =>
            x.PatientId == input.PatientId && x.Status == AppointmentStatuses.Booked &&
            x.SlotStartsAtUtc < slotEnd && x.SlotEndsAtUtc > slotStart, cancellationToken);
        if (patientOverlap)
            return AppointmentResult<AppointmentDto>.Failure("The patient already has an overlapping active appointment.", 409);

        var appointment = new Appointment
        {
            HospitalId = hospitalId,
            DepartmentId = session.DepartmentId,
            PatientId = input.PatientId,
            ProviderStaffId = session.ProviderStaffId,
            AvailabilitySessionId = session.Id,
            SlotStartsAtUtc = slotStart,
            SlotEndsAtUtc = slotEnd,
            Status = AppointmentStatuses.Booked,
            Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim()
        };
        db.Appointments.Add(appointment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: "23505" or "23P01" })
        {
            return AppointmentResult<AppointmentDto>.Failure("The selected slot is no longer available or overlaps an active appointment.", 409);
        }

        return AppointmentResult<AppointmentDto>.Success(ToDto(appointment));
    }

    public async Task<AppointmentResult<AppointmentDto>> GetAppointmentAsync(
        Guid hospitalId, Guid appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == appointmentId && x.HospitalId == hospitalId, cancellationToken);
        if (appointment is null || !CanReadHospital(hospitalId, appointment.DepartmentId))
            return AppointmentResult<AppointmentDto>.Failure("Appointment not found.", 404);
        return AppointmentResult<AppointmentDto>.Success(ToDto(appointment));
    }

    public async Task<AppointmentResult<AppointmentDto>> CancelAsync(
        Guid hospitalId, Guid appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(
            x => x.Id == appointmentId && x.HospitalId == hospitalId, cancellationToken);
        if (appointment is null || !organizationScope.CanManageDepartment(hospitalId, appointment.DepartmentId))
            return AppointmentResult<AppointmentDto>.Failure("Appointment not found.", 404);
        if (appointment.Status != AppointmentStatuses.Booked)
            return AppointmentResult<AppointmentDto>.Failure("Only booked appointments can be cancelled.", 409);

        appointment.Status = AppointmentStatuses.Cancelled;
        await db.SaveChangesAsync(cancellationToken);
        return AppointmentResult<AppointmentDto>.Success(ToDto(appointment));
    }

    private bool CanReadHospital(Guid hospitalId, Guid? departmentId)
    {
        if (hospitalId == Guid.Empty) return false;
        if (organizationScope.IsTenantWide) return true;
        return organizationScope.HospitalIds.Contains(hospitalId) ||
               (departmentId.HasValue && organizationScope.DepartmentIds.Contains(departmentId.Value));
    }

    private static AvailabilitySessionDto ToDto(ProviderAvailabilitySession x) =>
        new(x.Id, x.HospitalId, x.DepartmentId, x.ProviderStaffId, x.LocalDate,
            x.StartsAtUtc, x.EndsAtUtc, x.SlotDurationMinutes, x.TimeZoneId, x.CreatedAtUtc);

    private static AppointmentDto ToDto(Appointment x) =>
        new(x.Id, x.HospitalId, x.DepartmentId, x.PatientId, x.ProviderStaffId,
            x.AvailabilitySessionId, x.SlotStartsAtUtc, x.SlotEndsAtUtc,
            x.Status, x.Reason, x.CreatedAtUtc);
}
