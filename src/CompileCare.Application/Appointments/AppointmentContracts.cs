namespace CompileCare.Application.Appointments;

public static class AppointmentPermissions
{
    public const string AvailabilityRead = "availability.read";
    public const string AvailabilityManage = "availability.manage";
    public const string AppointmentRead = "appointment.read";
    public const string AppointmentManage = "appointment.manage";
}

public static class AppointmentStatuses
{
    public const string Booked = "Booked";
    public const string Cancelled = "Cancelled";
    public const string Completed = "Completed";
    public const string NoShow = "NoShow";
}

public sealed record AvailabilitySessionCreateInput(
    Guid DepartmentId,
    Guid ProviderStaffId,
    DateOnly LocalDate,
    TimeOnly StartLocalTime,
    TimeOnly EndLocalTime,
    int SlotDurationMinutes,
    string TimeZoneId);

public sealed record AvailabilitySessionDto(
    Guid Id,
    Guid HospitalId,
    Guid DepartmentId,
    Guid ProviderStaffId,
    DateOnly LocalDate,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    int SlotDurationMinutes,
    string TimeZoneId,
    DateTimeOffset CreatedAtUtc);

public sealed record AppointmentSlotDto(
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    bool IsAvailable);

public sealed record AppointmentBookInput(
    Guid AvailabilitySessionId,
    Guid PatientId,
    DateTimeOffset SlotStartsAtUtc,
    string? Reason);

public sealed record AppointmentDto(
    Guid Id,
    Guid HospitalId,
    Guid DepartmentId,
    Guid PatientId,
    Guid ProviderStaffId,
    Guid AvailabilitySessionId,
    DateTimeOffset SlotStartsAtUtc,
    DateTimeOffset SlotEndsAtUtc,
    string Status,
    string? Reason,
    DateTimeOffset CreatedAtUtc);

public sealed record AvailabilitySessionPage(
    IReadOnlyList<AvailabilitySessionDto> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}

public sealed record AppointmentResult<T>(T? Value, string? Error, int StatusCode)
{
    public bool IsSuccess => Error is null;
    public static AppointmentResult<T> Success(T value) => new(value, null, 200);
    public static AppointmentResult<T> Failure(string error, int statusCode) => new(default, error, statusCode);
}

public interface IAppointmentSchedulingService
{
    Task<AppointmentResult<AvailabilitySessionDto>> CreateAvailabilityAsync(
        Guid hospitalId, AvailabilitySessionCreateInput input, CancellationToken cancellationToken);
    Task<AppointmentResult<AvailabilitySessionPage>> ListAvailabilityAsync(
        Guid hospitalId, DateOnly localDate, Guid? departmentId, int page, int pageSize, CancellationToken cancellationToken);
    Task<AppointmentResult<IReadOnlyList<AppointmentSlotDto>>> GetSlotsAsync(
        Guid hospitalId, Guid sessionId, CancellationToken cancellationToken);
    Task<AppointmentResult<AppointmentDto>> BookAsync(
        Guid hospitalId, AppointmentBookInput input, CancellationToken cancellationToken);
    Task<AppointmentResult<AppointmentDto>> GetAppointmentAsync(
        Guid hospitalId, Guid appointmentId, CancellationToken cancellationToken);
    Task<AppointmentResult<AppointmentDto>> CancelAsync(
        Guid hospitalId, Guid appointmentId, CancellationToken cancellationToken);
}
