using CompileCare.Application.Clinical;

namespace CompileCare.Infrastructure.Persistence;

public sealed class OpdEncounter : TenantOwnedEntity
{
    public Guid HospitalId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid PatientId { get; set; }
    public Guid QueueEntryId { get; set; }
    public string Status { get; set; } = OpdEncounterStatuses.InProgress;
    public string? ChiefComplaint { get; set; }
    public string? History { get; set; }
    public string? Examination { get; set; }
    public string? Assessment { get; set; }
    public string? Plan { get; set; }
    public string CreatedByIssuer { get; set; } = string.Empty;
    public string CreatedBySubject { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public string? SignedByIssuer { get; set; }
    public string? SignedBySubject { get; set; }
    public DateTimeOffset? SignedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public string? CancellationReason { get; set; }
}

public sealed class OpdEncounterAmendment : TenantOwnedEntity
{
    public Guid EncounterId { get; set; }
    public Guid HospitalId { get; set; }
    public Guid DepartmentId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string AuthorIssuer { get; set; } = string.Empty;
    public string AuthorSubject { get; set; } = string.Empty;
}
