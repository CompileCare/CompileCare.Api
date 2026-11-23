namespace CompileCare.Application.Facilities;

public sealed record FacilityResult<T>(T? Value, string? Error, int StatusCode)
{
    public bool IsSuccess => Error is null;
    public static FacilityResult<T> Success(T value) => new(value, null, 200);
    public static FacilityResult<T> Failure(string error, int statusCode) => new(default, error, statusCode);
}
public sealed record DepartmentDto(Guid Id, Guid HospitalId, string Name, string? Code);
public sealed record WardDto(Guid Id, Guid DepartmentId, string Name);
public sealed record RoomDto(Guid Id, Guid WardId, string Name);
public sealed record BedDto(Guid Id, Guid RoomId, string Number, bool IsActive);
public sealed record DepartmentInput(string? Name, string? Code);
public sealed record NamedInput(string? Name);
public sealed record BedInput(string? Number, bool IsActive = true);

public interface IFacilityService
{
    Task<IReadOnlyList<DepartmentDto>> ListDepartments(Guid hospitalId, CancellationToken ct);
    Task<FacilityResult<DepartmentDto>> GetDepartment(Guid hospitalId, Guid id, CancellationToken ct);
    Task<FacilityResult<DepartmentDto>> CreateDepartment(Guid hospitalId, DepartmentInput input, CancellationToken ct);
    Task<FacilityResult<DepartmentDto>> UpdateDepartment(Guid hospitalId, Guid id, DepartmentInput input, CancellationToken ct);
    Task<FacilityResult<bool>> DeleteDepartment(Guid hospitalId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<WardDto>> ListWards(Guid departmentId, CancellationToken ct);
    Task<FacilityResult<WardDto>> GetWard(Guid departmentId, Guid id, CancellationToken ct);
    Task<FacilityResult<WardDto>> CreateWard(Guid departmentId, NamedInput input, CancellationToken ct);
    Task<FacilityResult<WardDto>> UpdateWard(Guid departmentId, Guid id, NamedInput input, CancellationToken ct);
    Task<FacilityResult<bool>> DeleteWard(Guid departmentId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<RoomDto>> ListRooms(Guid wardId, CancellationToken ct);
    Task<FacilityResult<RoomDto>> GetRoom(Guid wardId, Guid id, CancellationToken ct);
    Task<FacilityResult<RoomDto>> CreateRoom(Guid wardId, NamedInput input, CancellationToken ct);
    Task<FacilityResult<RoomDto>> UpdateRoom(Guid wardId, Guid id, NamedInput input, CancellationToken ct);
    Task<FacilityResult<bool>> DeleteRoom(Guid wardId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<BedDto>> ListBeds(Guid roomId, CancellationToken ct);
    Task<FacilityResult<BedDto>> GetBed(Guid roomId, Guid id, CancellationToken ct);
    Task<FacilityResult<BedDto>> CreateBed(Guid roomId, BedInput input, CancellationToken ct);
    Task<FacilityResult<BedDto>> UpdateBed(Guid roomId, Guid id, BedInput input, CancellationToken ct);
    Task<FacilityResult<bool>> DeleteBed(Guid roomId, Guid id, CancellationToken ct);
}