namespace HeapCare.Infrastructure.Persistence;

public sealed class ProviderAvailabilitySession : TenantOwnedEntity
{
    public Guid HospitalId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid ProviderStaffId { get; set; }
    public DateOnly LocalDate { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
    public int SlotDurationMinutes { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
}

public sealed class Appointment : TenantOwnedEntity
{
    public Guid HospitalId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid PatientId { get; set; }
    public Guid ProviderStaffId { get; set; }
    public Guid AvailabilitySessionId { get; set; }
    public DateTimeOffset SlotStartsAtUtc { get; set; }
    public DateTimeOffset SlotEndsAtUtc { get; set; }
    public string Status { get; set; } = "Booked";
    public string? Reason { get; set; }
    public Guid? RescheduledFromAppointmentId { get; set; }
}
