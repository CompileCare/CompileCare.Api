namespace HeapCare.Application.Clinical;

public static class OpdEncounterPermissions
{
    public const string Read = "encounter.read";
    public const string Manage = "encounter.manage";
    public const string Sign = "encounter.sign";
    public const string Amend = "encounter.amend";
}

public static class OpdEncounterStatuses
{
    public const string InProgress = "InProgress";
    public const string Signed = "Signed";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";
}

public sealed record OpdEncounterActor(string Issuer, string Subject);

public sealed record OpdEncounterNoteInput(
    string? ChiefComplaint,
    string? History,
    string? Examination,
    string? Assessment,
    string? Plan);

public sealed record OpdEncounterAmendmentInput(string Reason, string Content);
public sealed record OpdEncounterCancelInput(string Reason);

public sealed record OpdEncounterAmendmentDto(
    Guid Id,
    string Reason,
    string Content,
    string AuthorIssuer,
    string AuthorSubject,
    DateTimeOffset CreatedAtUtc);

public sealed record OpdEncounterDto(
    Guid Id,
    Guid HospitalId,
    Guid DepartmentId,
    Guid PatientId,
    Guid QueueEntryId,
    string Status,
    string? ChiefComplaint,
    string? History,
    string? Examination,
    string? Assessment,
    string? Plan,
    string CreatedByIssuer,
    string CreatedBySubject,
    DateTimeOffset StartedAtUtc,
    string? SignedByIssuer,
    string? SignedBySubject,
    DateTimeOffset? SignedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    string? CancellationReason,
    IReadOnlyList<OpdEncounterAmendmentDto> Amendments);

public sealed record OpdEncounterResult<T>(T? Value, string? Error, int StatusCode)
{
    public bool IsSuccess => Error is null;
    public static OpdEncounterResult<T> Success(T value) => new(value, null, 200);
    public static OpdEncounterResult<T> Failure(string error, int statusCode) => new(default, error, statusCode);
}

public interface IOpdEncounterService
{
    Task<OpdEncounterResult<OpdEncounterDto>> CreateFromQueueAsync(
        Guid hospitalId, Guid queueEntryId, OpdEncounterActor actor, CancellationToken cancellationToken);
    Task<OpdEncounterResult<OpdEncounterDto>> GetAsync(
        Guid hospitalId, Guid encounterId, CancellationToken cancellationToken);
    Task<OpdEncounterResult<OpdEncounterDto>> GetByQueueEntryAsync(
        Guid hospitalId, Guid queueEntryId, CancellationToken cancellationToken);
    Task<OpdEncounterResult<OpdEncounterDto>> UpdateNoteAsync(
        Guid hospitalId, Guid encounterId, OpdEncounterNoteInput input, OpdEncounterActor actor, CancellationToken cancellationToken);
    Task<OpdEncounterResult<OpdEncounterDto>> SignAsync(
        Guid hospitalId, Guid encounterId, OpdEncounterActor actor, CancellationToken cancellationToken);
    Task<OpdEncounterResult<OpdEncounterDto>> AddAmendmentAsync(
        Guid hospitalId, Guid encounterId, OpdEncounterAmendmentInput input, OpdEncounterActor actor, CancellationToken cancellationToken);
    Task<OpdEncounterResult<OpdEncounterDto>> CloseAsync(
        Guid hospitalId, Guid encounterId, CancellationToken cancellationToken);
    Task<OpdEncounterResult<OpdEncounterDto>> CancelAsync(
        Guid hospitalId, Guid encounterId, string reason, CancellationToken cancellationToken);
}
