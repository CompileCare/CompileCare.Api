namespace CompileCare.Application.Patients;

public static class PatientIdentifierPermissions
{
    public const string Read = "patient.identifier.read";
    public const string Assign = "patient.identifier.assign";
    public const string PolicyRead = "patient.identifier.policy.read";
    public const string PolicyManage = "patient.identifier.policy.manage";
}

public sealed record PatientIdentifierPolicyInput(string? Prefix, int NumberLength = 6);

public sealed record PatientIdentifierPolicyDto(
    Guid HospitalId,
    string Prefix,
    int NumberLength);

public sealed record PatientIdentifierDto(
    Guid Id,
    Guid PatientId,
    Guid HospitalId,
    string TypeCode,
    string Value,
    DateTimeOffset AssignedAtUtc);

public sealed record PatientIdentifierLookupDto(
    PatientDto Patient,
    PatientIdentifierDto Identifier);

public interface IPatientIdentifierService
{
    Task<PatientResult<PatientIdentifierPolicyDto>> CreatePolicyAsync(
        Guid hospitalId,
        PatientIdentifierPolicyInput input,
        CancellationToken cancellationToken);

    Task<PatientResult<PatientIdentifierPolicyDto>> GetPolicyAsync(
        Guid hospitalId,
        CancellationToken cancellationToken);

    Task<PatientResult<PatientIdentifierDto>> AssignMedicalRecordNumberAsync(
        Guid hospitalId,
        Guid patientId,
        CancellationToken cancellationToken);

    Task<PatientResult<IReadOnlyList<PatientIdentifierDto>>> GetPatientIdentifiersAsync(
        Guid hospitalId,
        Guid patientId,
        CancellationToken cancellationToken);

    Task<PatientResult<PatientIdentifierLookupDto>> FindByMedicalRecordNumberAsync(
        Guid hospitalId,
        string? medicalRecordNumber,
        CancellationToken cancellationToken);
}
