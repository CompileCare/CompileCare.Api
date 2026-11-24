using CompileCare.Application.Appointments;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Appointments;

public sealed class AppointmentQueueService(
    CompileCareDbContext db,
    IOrganizationScopeContext organizationScope,
    IAppointmentSchedulingService scheduling) : IAppointmentQueueService
{
    public async Task<AppointmentResult<AppointmentDto>> RescheduleAsync(
        Guid hospitalId, Guid appointmentId, AppointmentBookInput input, CancellationToken cancellationToken)
    {
        var current = await db.Appointments.FirstOrDefaultAsync(
            x => x.Id == appointmentId && x.HospitalId == hospitalId, cancellationToken);
        if (current is null || !organizationScope.CanManageDepartment(hospitalId, current.DepartmentId))
            return AppointmentResult<AppointmentDto>.Failure("Appointment not found.", 404);
        if (current.Status != AppointmentStatuses.Booked)
            return AppointmentResult<AppointmentDto>.Failure("Only booked appointments can be rescheduled.", 409);
        if (await db.QueueEntries.AnyAsync(x => x.AppointmentId == appointmentId, cancellationToken))
            return AppointmentResult<AppointmentDto>.Failure("A checked-in appointment cannot be rescheduled.", 409);

        current.Status = AppointmentStatuses.Cancelled;
        var replacement = await scheduling.BookAsync(hospitalId, input, cancellationToken);
        if (!replacement.IsSuccess)
        {
            current.Status = AppointmentStatuses.Booked;
            return replacement;
        }
        return replacement;
    }

    public async Task<AppointmentResult<QueueEntryDto>> CheckInAsync(
        Guid hospitalId, Guid appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(
            x => x.Id == appointmentId && x.HospitalId == hospitalId, cancellationToken);
        if (appointment is null || !organizationScope.CanManageDepartment(hospitalId, appointment.DepartmentId))
            return AppointmentResult<QueueEntryDto>.Failure("Appointment not found.", 404);

        var existing = await db.QueueEntries.FirstOrDefaultAsync(
            x => x.AppointmentId == appointmentId, cancellationToken);
        if (existing is not null)
            return AppointmentResult<QueueEntryDto>.Success(ToDto(existing));
        if (appointment.Status != AppointmentStatuses.Booked)
            return AppointmentResult<QueueEntryDto>.Failure("Only booked appointments can be checked in.", 409);

        var session = await db.ProviderAvailabilitySessions.FirstAsync(
            x => x.Id == appointment.AvailabilitySessionId, cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(session.TimeZoneId);
        var now = DateTimeOffset.UtcNow;
        if (now < appointment.SlotStartsAtUtc.AddMinutes(-60) ||
            now > appointment.SlotStartsAtUtc.AddMinutes(30))
            return AppointmentResult<QueueEntryDto>.Failure(
                "Check-in is allowed from 60 minutes before until 30 minutes after the appointment start.", 409);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var entry = await CreateQueueEntryAsync(
            hospitalId, appointment.DepartmentId, appointment.PatientId, appointment.Id,
            localDate, session.TimeZoneId, cancellationToken);
        return AppointmentResult<QueueEntryDto>.Success(ToDto(entry));
    }

    public async Task<AppointmentResult<QueueEntryDto>> CreateWalkInAsync(
        Guid hospitalId, WalkInCreateInput input, CancellationToken cancellationToken)
    {
        if (input.PatientId == Guid.Empty || input.DepartmentId == Guid.Empty ||
            !organizationScope.CanManageDepartment(hospitalId, input.DepartmentId))
            return AppointmentResult<QueueEntryDto>.Failure("Hospital or department not found.", 404);
        if (string.IsNullOrWhiteSpace(input.TimeZoneId) || input.TimeZoneId.Length > 100)
            return AppointmentResult<QueueEntryDto>.Failure("A valid hospital time zone is required.", 400);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId); }
        catch (TimeZoneNotFoundException) { return AppointmentResult<QueueEntryDto>.Failure("The time zone is not recognized.", 400); }
        catch (InvalidTimeZoneException) { return AppointmentResult<QueueEntryDto>.Failure("The time zone is invalid.", 400); }

        var registered = await db.HospitalPatientRegistrations.AnyAsync(
            x => x.HospitalId == hospitalId && x.PatientId == input.PatientId, cancellationToken);
        if (!registered)
            return AppointmentResult<QueueEntryDto>.Failure("The patient must be registered at this hospital before joining the queue.", 409);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var entry = await CreateQueueEntryAsync(
            hospitalId, input.DepartmentId, input.PatientId, null, localDate,
            input.TimeZoneId, cancellationToken);
        return AppointmentResult<QueueEntryDto>.Success(ToDto(entry));
    }

    public async Task<AppointmentResult<QueuePage>> ListQueueAsync(
        Guid hospitalId, Guid departmentId, DateOnly date, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        if (!organizationScope.CanManageDepartment(hospitalId, departmentId))
            return AppointmentResult<QueuePage>.Failure("Queue not found.", 404);
        page = Math.Max(1, Math.Min(page, 2_000_000));
        pageSize = pageSize < 1 ? 25 : Math.Min(pageSize, 100);
        var query = db.QueueEntries.AsNoTracking().Where(x =>
            x.HospitalId == hospitalId && x.DepartmentId == departmentId && x.QueueDateLocal == date);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.TokenNumber).Skip((page - 1) * pageSize)
            .Take(pageSize).Select(x => new QueueEntryDto(
                x.Id, x.HospitalId, x.DepartmentId, x.QueueDateLocal, x.TokenNumber,
                x.Status, x.CheckedInAtUtc!.Value, x.CalledAtUtc, x.StartedAtUtc, x.CompletedAtUtc))
            .ToListAsync(cancellationToken);
        return AppointmentResult<QueuePage>.Success(new(items, page, pageSize, total));
    }

    public async Task<AppointmentResult<QueueEntryDto>> TransitionAsync(
        Guid hospitalId, Guid queueEntryId, string action, QueueActionInput input,
        CancellationToken cancellationToken)
    {
        var entry = await db.QueueEntries.FirstOrDefaultAsync(
            x => x.Id == queueEntryId && x.HospitalId == hospitalId, cancellationToken);
        if (entry is null || !organizationScope.CanManageDepartment(hospitalId, entry.DepartmentId))
            return AppointmentResult<QueueEntryDto>.Failure("Queue entry not found.", 404);
        var now = DateTimeOffset.UtcNow;
        switch (action.ToLowerInvariant())
        {
            case "call" when entry.Status == QueueEntryStatuses.Waiting:
                entry.Status = QueueEntryStatuses.Called; entry.CalledAtUtc = now; break;
            case "recall" when entry.Status == QueueEntryStatuses.Called:
                entry.Status = QueueEntryStatuses.Waiting; entry.CalledAtUtc = null; break;
            case "start" when entry.Status == QueueEntryStatuses.Called:
                entry.Status = QueueEntryStatuses.InProgress; entry.StartedAtUtc = now; break;
            case "complete" when entry.Status == QueueEntryStatuses.InProgress:
                entry.Status = QueueEntryStatuses.Completed; entry.CompletedAtUtc = now; break;
            case "cancel" when entry.Status is QueueEntryStatuses.Waiting or QueueEntryStatuses.Called:
                entry.Status = QueueEntryStatuses.Cancelled; break;
            case "noshow" when entry.Status is QueueEntryStatuses.Waiting or QueueEntryStatuses.Called:
                entry.Status = QueueEntryStatuses.NoShow; break;
            default:
                return AppointmentResult<QueueEntryDto>.Failure("The requested queue transition is not valid from the current state.", 409);
        }
        if (action.Equals("cancel", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("noshow", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 250)
                return AppointmentResult<QueueEntryDto>.Failure("A reason of 1 to 250 characters is required.", 400);
            entry.ClosureReason = input.Reason.Trim();
        }
        await db.SaveChangesAsync(cancellationToken);
        return AppointmentResult<QueueEntryDto>.Success(ToDto(entry));
    }

    private async Task<QueueEntry> CreateQueueEntryAsync(
        Guid hospitalId, Guid departmentId, Guid patientId, Guid? appointmentId,
        DateOnly localDate, string timeZoneId, CancellationToken cancellationToken)
    {
        var lockKey = $"{db.CurrentTenantId}:{hospitalId}:{departmentId}:{localDate:yyyyMMdd}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        var existing = appointmentId.HasValue
            ? await db.QueueEntries.FirstOrDefaultAsync(x => x.AppointmentId == appointmentId, cancellationToken)
            : null;
        if (existing is not null) return existing;
        var lastToken = await db.QueueEntries.Where(x =>
                x.HospitalId == hospitalId && x.DepartmentId == departmentId && x.QueueDateLocal == localDate)
            .Select(x => (int?)x.TokenNumber).MaxAsync(cancellationToken) ?? 0;
        var entry = new QueueEntry
        {
            HospitalId = hospitalId, DepartmentId = departmentId, PatientId = patientId,
            AppointmentId = appointmentId, QueueDateLocal = localDate, TimeZoneId = timeZoneId,
            TokenNumber = checked(lastToken + 1), Status = QueueEntryStatuses.Waiting,
            CheckedInAtUtc = DateTimeOffset.UtcNow
        };
        db.QueueEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        return entry;
    }

    private static QueueEntryDto ToDto(QueueEntry x) => new(
        x.Id, x.HospitalId, x.DepartmentId, x.QueueDateLocal, x.TokenNumber,
        x.Status, x.CheckedInAtUtc!.Value, x.CalledAtUtc, x.StartedAtUtc, x.CompletedAtUtc);
}
