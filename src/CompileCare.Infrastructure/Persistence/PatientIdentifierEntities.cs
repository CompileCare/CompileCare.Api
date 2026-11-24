namespace CompileCare.Infrastructure.Persistence;

public static class PatientIdentifierTypes
{
    public const string MedicalRecordNumber = "MRN";
}

public sealed class PatientIdentifier : TenantOwnedEntity
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid HospitalId { get; set; }
    public Hospital Hospital { get; set; } = null!;
    public string TypeCode { get; set; } = PatientIdentifierTypes.MedicalRecordNumber;
    public string Value { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public DateTimeOffset AssignedAtUtc { get; set; }
}

public sealed class HospitalPatientIdentifierPolicy : TenantOwnedEntity
{
    public Guid HospitalId { get; set; }
    public Hospital Hospital { get; set; } = null!;
    public string Prefix { get; set; } = string.Empty;
    public int NumberLength { get; set; }
    public long NextNumber { get; set; } = 1;
}
