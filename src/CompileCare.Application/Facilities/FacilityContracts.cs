namespace CompileCare.Application.Facilities;

public sealed record FacilityResult<T>(T? Value, string? Error, int StatusCode)
{
    public bool IsSuccess => Error is null;
    public static FacilityResult<T> Success(T value) => new(value, null, 200);
    public static FacilityResult<T> Failure(string error, int statusCode) => new(default, error, statusCode);
}
public sealed record PageRequest(int Page = 1, int PageSize = 50)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    public int SafePage => Page < 1 ? 1 : Math.Min(Page, int.MaxValue / MaxPageSize);
    public int SafePageSize => PageSize < 1 ? DefaultPageSize : Math.Min(PageSize, MaxPageSize);
    public int Skip => (SafePage - 1) * SafePageSize;
}
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}
public sealed record HospitalDto(Guid Id, string Name, string? Code);
public sealed record HospitalInput(string? Name, string? Code);
public sealed record DepartmentDto(Guid Id, Guid HospitalId, string Name, string? Code);
public sealed record WardDto(Guid Id, Guid DepartmentId, string Name);
public sealed record RoomDto(Guid Id, Guid WardId, string Name);
public sealed record BedDto(Guid Id, Guid RoomId, string Number, bool IsActive);
public sealed record DepartmentInput(string? Name, string? Code);
public sealed record NamedInput(string? Name);
public sealed record BedInput(string? Number, bool IsActive = true);

public interface IHospitalService
{
    Task<PagedResult<HospitalDto>> ListHospitals(PageRequest page, CancellationToken ct);
    Task<FacilityResult<HospitalDto>> GetHospital(Guid id, CancellationToken ct);
    Task<FacilityResult<HospitalDto>> CreateHospital(HospitalInput input, CancellationToken ct);
    Task<FacilityResult<HospitalDto>> UpdateHospital(Guid id, HospitalInput input, CancellationToken ct);
    Task<FacilityResult<bool>> DeleteHospital(Guid id, CancellationToken ct);
}
public interface IDepartmentService
{
    Task<PagedResult<DepartmentDto>> ListDepartments(Guid hospitalId, PageRequest page, CancellationToken ct);
    Task<FacilityResult<DepartmentDto>> GetDepartment(Guid hospitalId, Guid id, CancellationToken ct);
    Task<FacilityResult<DepartmentDto>> CreateDepartment(Guid hospitalId, DepartmentInput input, CancellationToken ct);
    Task<FacilityResult<DepartmentDto>> UpdateDepartment(Guid hospitalId, Guid id, DepartmentInput input, CancellationToken ct);
    Task<FacilityResult<bool>> DeleteDepartment(Guid hospitalId, Guid id, CancellationToken ct);
}
public interface IWardService
{
    Task<PagedResult<WardDto>> ListWards(Guid departmentId, PageRequest page, CancellationToken ct);
    Task<FacilityResult<WardDto>> GetWard(Guid departmentId, Guid id, CancellationToken ct);
    Task<FacilityResult<WardDto>> CreateWard(Guid departmentId, NamedInput input, CancellationToken ct);
    Task<FacilityResult<WardDto>> UpdateWard(Guid departmentId, Guid id, NamedInput input, CancellationToken ct);
    Task<FacilityResult<bool>> DeleteWard(Guid departmentId, Guid id, CancellationToken ct);
}
public interface IRoomService
{
    Task<PagedResult<RoomDto>> ListRooms(Guid wardId, PageRequest page, CancellationToken ct);
    Task<FacilityResult<RoomDto>> GetRoom(Guid wardId, Guid id, CancellationToken ct);
    Task<FacilityResult<RoomDto>> CreateRoom(Guid wardId, NamedInput input, CancellationToken ct);
    Task<FacilityResult<RoomDto>> UpdateRoom(Guid wardId, Guid id, NamedInput input, CancellationToken ct);
    Task<FacilityResult<bool>> DeleteRoom(Guid wardId, Guid id, CancellationToken ct);
}
public interface IBedService
{
    Task<PagedResult<BedDto>> ListBeds(Guid roomId, PageRequest page, CancellationToken ct);
    Task<FacilityResult<BedDto>> GetBed(Guid roomId, Guid id, CancellationToken ct);
    Task<FacilityResult<BedDto>> CreateBed(Guid roomId, BedInput input, CancellationToken ct);
    Task<FacilityResult<BedDto>> UpdateBed(Guid roomId, Guid id, BedInput input, CancellationToken ct);
    Task<FacilityResult<bool>> DeleteBed(Guid roomId, Guid id, CancellationToken ct);
}
public interface IFacilityService : IHospitalService, IDepartmentService, IWardService, IRoomService, IBedService
{
}
