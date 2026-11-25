namespace CompileCare.Application.Tenancy;

public static class TenantStatuses
{
    public const string Provisioning = "Provisioning";
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Closed = "Closed";

    public static bool IsKnown(string status) =>
        status is Provisioning or Active or Suspended or Closed;

    public static bool CanTransition(string current, string target) =>
        (current, target) switch
        {
            (Provisioning, Active) => true,
            (Provisioning, Closed) => true,
            (Active, Suspended) => true,
            (Active, Closed) => true,
            (Suspended, Active) => true,
            (Suspended, Closed) => true,
            _ => false
        };
}

public sealed record TenantDto(
    Guid Id, string Name, string Slug, string Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record TenantCreateInput(string? Name, string? Slug);

public sealed record TenantRegistryResult<T>(T? Value, string? Error, int StatusCode)
{
    public bool IsSuccess => Error is null;
    public static TenantRegistryResult<T> Success(T value) => new(value, null, 200);
    public static TenantRegistryResult<T> Failure(string error, int statusCode) =>
        new(default, error, statusCode);
}

public sealed record TenantPageRequest(int Page = 1, int PageSize = 50)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    public int SafePage => Page < 1 ? 1 : Math.Min(Page, int.MaxValue / MaxPageSize);
    public int SafePageSize => PageSize < 1 ? DefaultPageSize : Math.Min(PageSize, MaxPageSize);
    public int Skip => (SafePage - 1) * SafePageSize;
}

public sealed record TenantPage<T>(
    IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}

public interface ITenantRegistryService
{
    Task<TenantPage<TenantDto>> ListAsync(TenantPageRequest page, CancellationToken cancellationToken);
    Task<TenantRegistryResult<TenantDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<TenantRegistryResult<TenantDto>> CreateAsync(TenantCreateInput input, CancellationToken cancellationToken);
    Task<TenantRegistryResult<TenantDto>> TransitionAsync(Guid id, string targetStatus, CancellationToken cancellationToken);
}

public interface IPlatformOperatorReader
{
    Task<bool> IsActiveAsync(string issuer, string subject, CancellationToken cancellationToken);
}


public sealed record PlatformOperatorBootstrapResult(bool Created, bool AlreadyInitialized, bool InvalidSecret);

public interface IPlatformOperatorBootstrapService
{
    Task<PlatformOperatorBootstrapResult> BootstrapAsync(
        string issuer,
        string subject,
        string configuredSecret,
        string suppliedSecret,
        CancellationToken cancellationToken);
}
