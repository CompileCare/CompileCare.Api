namespace CompileCare.Application.Staff;

public static class StaffPermissions
{
    public const string Read = "staff.read";
    public const string Manage = "staff.manage";
}

public sealed record StaffProfileCreateInput(
    string? StaffCode,
    string? GivenName,
    string? MiddleName,
    string? FamilyName,
    string? ProviderDesignation,
    Guid HospitalId,
    Guid? DepartmentId,
    DateOnly? EffectiveFrom);

public sealed record StaffProfileUpdateInput(
    string? GivenName,
    string? MiddleName,
    string? FamilyName,
    string? ProviderDesignation);

public sealed record StaffAssignmentInput(
    Guid HospitalId,
    Guid? DepartmentId,
    DateOnly EffectiveFrom);

public sealed record StaffAssignmentEndInput(DateOnly EffectiveTo);

public sealed record StaffAssignmentDto(
    Guid Id,
    Guid HospitalId,
    Guid? DepartmentId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive);

public sealed record StaffProfileDto(
    Guid Id,
    string StaffCode,
    string GivenName,
    string? MiddleName,
    string? FamilyName,
    string? ProviderDesignation,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<StaffAssignmentDto> Assignments);

public sealed record StaffSearchRequest(string? Query = null, int Page = 1, int PageSize = 50)
{
    public int SafePageSize => PageSize < 1 ? 50 : Math.Min(PageSize, 100);
    public int SafePage => Page < 1 ? 1 : Math.Min(Page, int.MaxValue / SafePageSize);
    public int Skip => (SafePage - 1) * SafePageSize;
}

public sealed record StaffPage(
    IReadOnlyList<StaffProfileDto> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}

public sealed record StaffResult<T>(T? Value, string? Error, int StatusCode)
{
    public bool IsSuccess => Error is null;
    public static StaffResult<T> Success(T value) => new(value, null, 200);
    public static StaffResult<T> Failure(string error, int statusCode) => new(default, error, statusCode);
}

public interface IStaffManagementService
{
    Task<StaffResult<StaffProfileDto>> CreateAsync(StaffProfileCreateInput input, CancellationToken cancellationToken);
    Task<StaffResult<StaffProfileDto>> GetAsync(Guid staffId, CancellationToken cancellationToken);
    Task<StaffResult<StaffPage>> ListAsync(StaffSearchRequest request, CancellationToken cancellationToken);
    Task<StaffResult<StaffProfileDto>> UpdateAsync(Guid staffId, StaffProfileUpdateInput input, CancellationToken cancellationToken);
    Task<StaffResult<StaffProfileDto>> SetActiveAsync(Guid staffId, bool isActive, CancellationToken cancellationToken);
    Task<StaffResult<StaffAssignmentDto>> AddAssignmentAsync(Guid staffId, StaffAssignmentInput input, CancellationToken cancellationToken);
    Task<StaffResult<StaffAssignmentDto>> EndAssignmentAsync(Guid staffId, Guid assignmentId, StaffAssignmentEndInput input, CancellationToken cancellationToken);
}
