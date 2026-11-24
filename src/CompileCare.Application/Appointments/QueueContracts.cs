namespace CompileCare.Application.Appointments;

public static class QueuePermissions
{
    public const string Read = "queue.read";
    public const string Manage = "queue.manage";
}

public sealed record QueueEntryDto(
    Guid Id,
    Guid HospitalId,
    Guid DepartmentId,
    DateOnly QueueDateLocal,
    int TokenNumber,
    string Status,
    DateTimeOffset CheckedInAtUtc,
    DateTimeOffset? CalledAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record WalkInCreateInput(Guid DepartmentId, Guid PatientId, string TimeZoneId);

public sealed record QueuePage(
    IReadOnlyList<QueueEntryDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record QueueActionInput(string? Reason);

public interface IAppointmentQueueService
{
    Task<AppointmentResult<AppointmentDto>> RescheduleAsync(Guid hospitalId, Guid appointmentId, AppointmentBookInput input, CancellationToken cancellationToken);
    Task<AppointmentResult<QueueEntryDto>> CheckInAsync(Guid hospitalId, Guid appointmentId, CancellationToken cancellationToken);
    Task<AppointmentResult<QueueEntryDto>> CreateWalkInAsync(Guid hospitalId, WalkInCreateInput input, CancellationToken cancellationToken);
    Task<AppointmentResult<QueuePage>> ListQueueAsync(Guid hospitalId, Guid departmentId, DateOnly date, int page, int pageSize, CancellationToken cancellationToken);
    Task<AppointmentResult<QueueEntryDto>> TransitionAsync(Guid hospitalId, Guid queueEntryId, string action, QueueActionInput input, CancellationToken cancellationToken);
}
