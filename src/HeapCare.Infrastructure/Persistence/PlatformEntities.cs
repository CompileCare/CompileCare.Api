namespace HeapCare.Infrastructure.Persistence;

/// <summary>Global platform-owned records. These are deliberately not tenant filtered.</summary>
public interface IPlatformEntity
{
    Guid Id { get; set; }
    DateTimeOffset CreatedAtUtc { get; set; }
    DateTimeOffset UpdatedAtUtc { get; set; }
    Guid Version { get; set; }
}

/// <summary>Customer/contract boundary for HeapCare. A tenant may own one or more hospitals.</summary>
public sealed class Tenant : IPlatformEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// A globally unique external OIDC principal. This record is not an authorization
/// grant; tenant membership and platform-operator privileges remain independent.
/// </summary>
public sealed class UserIdentity : IPlatformEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
}

/// <summary>A trusted HeapCare platform operator, keyed by a validated external OIDC identity.</summary>
public sealed class PlatformOperator : IPlatformEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid UserIdentityId { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
