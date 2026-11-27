namespace HeapCare.Infrastructure.Persistence;

public sealed class Patient : TenantOwnedEntity
{
    public string GivenName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string? FamilyName { get; set; }
    public string NormalizedGivenName { get; set; } = string.Empty;
    public string? NormalizedMiddleName { get; set; }
    public string? NormalizedFamilyName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public ICollection<HospitalPatientRegistration> HospitalRegistrations { get; set; } = [];
}

public sealed class HospitalPatientRegistration : TenantOwnedEntity
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid HospitalId { get; set; }
    public Hospital Hospital { get; set; } = null!;
    public DateTimeOffset RegisteredAtUtc { get; set; }
}
