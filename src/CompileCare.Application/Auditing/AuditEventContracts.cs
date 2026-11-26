namespace CompileCare.Application.Auditing;

public static class AuditPermissions
{
    public const string Read = "audit.read";
}

public sealed record AuditEventQuery(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Action = null,
    string? ResourceType = null,
    Guid? ResourceId = null,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditEventDto(
    Guid Id,
    Guid? TenantId,
    Guid? RequestedTenantId,
    string? ActorIssuer,
    string? ActorSubject,
    string Action,
    string ResourceType,
    Guid? ResourceId,
    string HttpMethod,
    string RouteTemplate,
    string Outcome,
    int StatusCode,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc);

public sealed record AuditEventPage(
    IReadOnlyList<AuditEventDto> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}

public sealed record AuditEventQueryResult<T>(T? Value, string? Error, int StatusCode)
{
    public bool IsSuccess => Error is null;
    public static AuditEventQueryResult<T> Success(T value) => new(value, null, 200);
    public static AuditEventQueryResult<T> Failure(string error, int statusCode) => new(default, error, statusCode);
}

public interface IAuditEventQueryService
{
    Task<AuditEventQueryResult<AuditEventPage>> ListAsync(AuditEventQuery query, CancellationToken cancellationToken);
    Task<AuditEventQueryResult<string>> ExportCsvAsync(AuditEventQuery query, CancellationToken cancellationToken);
}
