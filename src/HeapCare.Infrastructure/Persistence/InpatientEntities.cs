namespace HeapCare.Infrastructure.Persistence;

public static class InpatientAdmissionStatuses
{
    public const string Active = "Active";
    public const string Cancelled = "Cancelled";
    public const string Discharged = "Discharged";
}

public sealed class InpatientAdmission : TenantOwnedEntity
{
    public Guid HospitalId { get; set; }
    public Guid PatientId { get; set; }
    public string Status { get; set; } = InpatientAdmissionStatuses.Active;
    public DateTimeOffset AdmittedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public DateTimeOffset? DischargedAtUtc { get; set; }

    public ICollection<InpatientBedPlacement> BedPlacements { get; set; } = [];
}

public sealed class InpatientBedPlacement : TenantOwnedEntity
{
    public Guid HospitalId { get; set; }
    public Guid AdmissionId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid WardId { get; set; }
    public Guid RoomId { get; set; }
    public Guid BedId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public string? EndReason { get; set; }
}
