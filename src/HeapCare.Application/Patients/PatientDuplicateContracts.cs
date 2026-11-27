namespace HeapCare.Application.Patients;

public static class PatientDuplicatePermissions
{
    public const string Review = "patient.duplicate.review";
}

public sealed record PatientDuplicateCheckRequest(
    string? GivenName,
    string? MiddleName,
    string? FamilyName,
    DateOnly? DateOfBirth);

public sealed record PatientDuplicateCandidateDto(
    Guid PatientId,
    string GivenName,
    string? MiddleName,
    string? FamilyName,
    DateOnly DateOfBirth,
    DateTimeOffset RegisteredAtUtc,
    IReadOnlyList<string> MatchSignals);

public sealed record PatientDuplicateCheckDto(
    bool Evaluated,
    string? EvaluationMessage,
    IReadOnlyList<PatientDuplicateCandidateDto> Candidates,
    bool HasMoreCandidates);

public interface IPatientDuplicateDetectionService
{
    Task<PatientResult<PatientDuplicateCheckDto>> FindCandidatesAsync(
        Guid hospitalId,
        PatientDuplicateCheckRequest request,
        CancellationToken cancellationToken);
}
