namespace HeapCare.Application.Inpatient;

/// <summary>
/// Dedicated inpatient permissions. They are intentionally not mapped to existing
/// OPD, patient-registration, facility-management or billing permissions.
/// Role grants remain closed until the hospital-approved IPD role matrix is accepted.
/// </summary>
public static class InpatientPermissions
{
    public const string Read = "inpatient.read";
    public const string Admit = "inpatient.admit";
    public const string BedAssign = "inpatient.bed.assign";
    public const string BedTransfer = "inpatient.bed.transfer";
    public const string CancelAdmission = "inpatient.admission.cancel";
    public const string Discharge = "inpatient.admission.discharge";
}

/// <summary>Validated OIDC actor identity supplied by the authenticated API boundary.</summary>
public sealed record InpatientActor(string Issuer, string Subject);

public sealed record InpatientOperationResult<T>(T? Value, string? Error, int StatusCode)
{
    public bool IsSuccess => Error is null;

    public static InpatientOperationResult<T> Success(T value) =>
        new(value, null, 200);

    public static InpatientOperationResult<T> Failure(string error, int statusCode) =>
        new(default, error, statusCode);
}

/// <summary>Starts an inpatient episode for a patient already registered at the selected hospital.</summary>
public sealed record InpatientAdmissionInput(Guid PatientId);

public sealed record InpatientBedLocation(
    Guid DepartmentId,
    Guid WardId,
    Guid RoomId,
    Guid BedId);


public sealed record InpatientBedTransferInput(InpatientBedLocation Destination, string Reason);

public interface IInpatientService
{
    Task<InpatientOperationResult<InpatientAdmissionDto>> CreateAdmissionAsync(
        Guid hospitalId, InpatientAdmissionInput input, InpatientActor actor,
        CancellationToken cancellationToken);
    Task<InpatientOperationResult<InpatientAdmissionDto>> GetAsync(
        Guid hospitalId, Guid admissionId, CancellationToken cancellationToken);
    Task<InpatientOperationResult<InpatientAdmissionDto>> AssignBedAsync(
        Guid hospitalId, Guid admissionId, InpatientBedLocation location,
        InpatientActor actor, CancellationToken cancellationToken);
    Task<InpatientOperationResult<InpatientAdmissionDto>> TransferBedAsync(
        Guid hospitalId, Guid admissionId, InpatientBedTransferInput input,
        InpatientActor actor, CancellationToken cancellationToken);
}

public sealed record InpatientBedPlacementDto(
    Guid Id,
    Guid AdmissionId,
    Guid HospitalId,
    InpatientBedLocation Location,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    string? EndReason);

public sealed record InpatientAdmissionDto(
    Guid Id,
    Guid HospitalId,
    Guid PatientId,
    string Status,
    DateTimeOffset AdmittedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    DateTimeOffset? DischargedAtUtc,
    InpatientBedPlacementDto? CurrentPlacement,
    IReadOnlyList<InpatientBedPlacementDto> PlacementHistory);
