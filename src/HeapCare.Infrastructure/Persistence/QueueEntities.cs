namespace HeapCare.Infrastructure.Persistence;

public static class QueueEntryStatuses
{
    public const string Waiting = "Waiting";
    public const string Called = "Called";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
}

public sealed class QueueEntry : TenantOwnedEntity
{
    public Guid HospitalId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public DateOnly QueueDateLocal { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public int TokenNumber { get; set; }
    public string Status { get; set; } = QueueEntryStatuses.Waiting;
    public DateTimeOffset? CheckedInAtUtc { get; set; }
    public DateTimeOffset? CalledAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ClosureReason { get; set; }
}
