namespace HeapCare.Infrastructure.Persistence;

/// <summary>
/// Append-only audit record. TenantId is populated only from the validated request
/// tenant context; RequestedTenantId is explicitly untrusted caller input.
/// No request/response body or clinical payload is stored.
/// </summary>
public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }
    public Guid? RequestedTenantId { get; set; }
    public string? ActorIssuer { get; set; }
    public string? ActorSubject { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public Guid? ResourceId { get; set; }
    public string HttpMethod { get; set; } = string.Empty;
    public string RouteTemplate { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
}
