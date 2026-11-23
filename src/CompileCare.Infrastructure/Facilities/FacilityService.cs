using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Facilities;

public sealed class FacilityService(CompileCareDbContext db) : IFacilityService
{
    private static FacilityResult<T> Missing<T>() => FacilityResult<T>.Failure("Resource not found.", 404);
    private static FacilityResult<T> Invalid<T>(string field, string message) => FacilityResult<T>.Failure($"{field}: {message}", 400);
    private static FacilityResult<T> Duplicate<T>(string message) => FacilityResult<T>.Failure(message, 409);
    private static FacilityResult<T> Concurrency<T>() => FacilityResult<T>.Failure("Resource changed concurrently. Reload and retry.", 409);
    private static bool Unique(DbUpdateException e) => e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private static bool ForeignKey(DbUpdateException e) => e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
    private static string? Clean(string? s) => s?.Trim();
    private static string? CleanCode(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();

    public async Task<IReadOnlyList<DepartmentDto>> ListDepartments(Guid hospitalId, CancellationToken ct) =>
        await db.Departments.AsNoTracking().Where(x=>x.HospitalId==hospitalId).OrderBy(x=>x.Name)
            .Select(x=>new DepartmentDto(x.Id,x.HospitalId,x.Name,x.Code)).ToListAsync(ct);
    public async Task<FacilityResult<DepartmentDto>> GetDepartment(Guid hospitalId, Guid id, CancellationToken ct) {
        var x=await db.Departments.AsNoTracking().FirstOrDefaultAsync(x=>x.HospitalId==hospitalId&&x.Id==id,ct);
        return x is null?Missing<DepartmentDto>():FacilityResult<DepartmentDto>.Success(new(x.Id,x.HospitalId,x.Name,x.Code));
    }
    public async Task<FacilityResult<DepartmentDto>> CreateDepartment(Guid hospitalId, DepartmentInput input, CancellationToken ct) {
        if(!await db.Hospitals.AnyAsync(x=>x.Id==hospitalId,ct)) return Missing<DepartmentDto>();
        var name=Clean(input.Name); var code=CleanCode(input.Code);
        if(string.IsNullOrWhiteSpace(name)||name.Length>200) return Invalid<DepartmentDto>("name","required, max 200 chars");
        if(code?.Length>50) return Invalid<DepartmentDto>("code","max 50 chars");
        if(await db.Departments.AnyAsync(x=>x.HospitalId==hospitalId&&(x.Name==name||(code!=null&&x.Code==code)),ct)) return Duplicate<DepartmentDto>("Department name/code already exists.");
        var e=new Department{HospitalId=hospitalId,Name=name,Code=code}; db.Add(e);
        try { await db.SaveChangesAsync(ct); return FacilityResult<DepartmentDto>.Success(new(e.Id,e.HospitalId,e.Name,e.Code)); }
        catch(DbUpdateException ex) when(Unique(ex)){return Duplicate<DepartmentDto>("Department name/code already exists.");}
    }
    public async Task<FacilityResult<DepartmentDto>> UpdateDepartment(Guid hospitalId, Guid id, DepartmentInput input, CancellationToken ct) {
        var e=await db.Departments.FirstOrDefaultAsync(x=>x.HospitalId==hospitalId&&x.Id==id,ct); if(e is null)return Missing<DepartmentDto>();
        var name=Clean(input.Name);var code=CleanCode(input.Code);
        if(string.IsNullOrWhiteSpace(name)||name.Length>200)return Invalid<DepartmentDto>("name","required, max 200 chars");
        if(code?.Length>50)return Invalid<DepartmentDto>("code","max 50 chars");
        if(await db.Departments.AnyAsync(x=>x.HospitalId==hospitalId&&x.Id!=id&&(x.Name==name||(code!=null&&x.Code==code)),ct))return Duplicate<DepartmentDto>("Department name/code already exists.");
        e.Name=name;e.Code=code;
        try{await db.SaveChangesAsync(ct);return FacilityResult<DepartmentDto>.Success(new(e.Id,e.HospitalId,e.Name,e.Code));}
        catch(DbUpdateConcurrencyException){return Concurrency<DepartmentDto>();}
        catch(DbUpdateException ex) when(Unique(ex)){return Duplicate<DepartmentDto>("Department name/code already exists.");}
    }
    public async Task<FacilityResult<bool>> DeleteDepartment(Guid hospitalId,Guid id,CancellationToken ct) {
        var e=await db.Departments.FirstOrDefaultAsync(x=>x.HospitalId==hospitalId&&x.Id==id,ct);if(e is null)return Missing<bool>();
        db.Remove(e);try{await db.SaveChangesAsync(ct);return FacilityResult<bool>.Success(true);}
        catch(DbUpdateConcurrencyException){return Concurrency<bool>();}
        catch(DbUpdateException ex) when(ForeignKey(ex)){return Duplicate<bool>("Department contains wards and cannot be deleted.");}
    }

    public async Task<IReadOnlyList<WardDto>> ListWards(Guid p,CancellationToken ct)=>await db.Wards.AsNoTracking().Where(x=>x.DepartmentId==p).OrderBy(x=>x.Name).Select(x=>new WardDto(x.Id,x.DepartmentId,x.Name)).ToListAsync(ct);
    public async Task<FacilityResult<WardDto>> GetWard(Guid p,Guid id,CancellationToken ct){var x=await db.Wards.AsNoTracking().FirstOrDefaultAsync(x=>x.DepartmentId==p&&x.Id==id,ct);return x is null?Missing<WardDto>():FacilityResult<WardDto>.Success(new(x.Id,x.DepartmentId,x.Name));}
    public async Task<FacilityResult<WardDto>> CreateWard(Guid p,NamedInput input,CancellationToken ct){if(!await db.Departments.AnyAsync(x=>x.Id==p,ct))return Missing<WardDto>();var n=Clean(input.Name);if(string.IsNullOrWhiteSpace(n)||n.Length>200)return Invalid<WardDto>("name","required, max 200 chars");if(await db.Wards.AnyAsync(x=>x.DepartmentId==p&&x.Name==n,ct))return Duplicate<WardDto>("Ward name already exists.");var e=new Ward{DepartmentId=p,Name=n};db.Add(e);try{await db.SaveChangesAsync(ct);return FacilityResult<WardDto>.Success(new(e.Id,e.DepartmentId,e.Name));}catch(DbUpdateException ex)when(Unique(ex)){return Duplicate<WardDto>("Ward name already exists.");}}
    public async Task<FacilityResult<WardDto>> UpdateWard(Guid p,Guid id,NamedInput input,CancellationToken ct){var e=await db.Wards.FirstOrDefaultAsync(x=>x.DepartmentId==p&&x.Id==id,ct);if(e is null)return Missing<WardDto>();var n=Clean(input.Name);if(string.IsNullOrWhiteSpace(n)||n.Length>200)return Invalid<WardDto>("name","required, max 200 chars");if(await db.Wards.AnyAsync(x=>x.DepartmentId==p&&x.Id!=id&&x.Name==n,ct))return Duplicate<WardDto>("Ward name already exists.");e.Name=n;try{await db.SaveChangesAsync(ct);return FacilityResult<WardDto>.Success(new(e.Id,e.DepartmentId,e.Name));}catch(DbUpdateConcurrencyException){return Concurrency<WardDto>();}catch(DbUpdateException ex)when(Unique(ex)){return Duplicate<WardDto>("Ward name already exists.");}}
    public async Task<FacilityResult<bool>> DeleteWard(Guid p,Guid id,CancellationToken ct){var e=await db.Wards.FirstOrDefaultAsync(x=>x.DepartmentId==p&&x.Id==id,ct);if(e is null)return Missing<bool>();db.Remove(e);try{await db.SaveChangesAsync(ct);return FacilityResult<bool>.Success(true);}catch(DbUpdateConcurrencyException){return Concurrency<bool>();}catch(DbUpdateException ex)when(ForeignKey(ex)){return Duplicate<bool>("Ward contains rooms and cannot be deleted.");}}

    public async Task<IReadOnlyList<RoomDto>> ListRooms(Guid p,CancellationToken ct)=>await db.Rooms.AsNoTracking().Where(x=>x.WardId==p).OrderBy(x=>x.Name).Select(x=>new RoomDto(x.Id,x.WardId,x.Name)).ToListAsync(ct);
    public async Task<FacilityResult<RoomDto>> GetRoom(Guid p,Guid id,CancellationToken ct){var x=await db.Rooms.AsNoTracking().FirstOrDefaultAsync(x=>x.WardId==p&&x.Id==id,ct);return x is null?Missing<RoomDto>():FacilityResult<RoomDto>.Success(new(x.Id,x.WardId,x.Name));}
    public async Task<FacilityResult<RoomDto>> CreateRoom(Guid p,NamedInput input,CancellationToken ct){if(!await db.Wards.AnyAsync(x=>x.Id==p,ct))return Missing<RoomDto>();var n=Clean(input.Name);if(string.IsNullOrWhiteSpace(n)||n.Length>100)return Invalid<RoomDto>("name","required, max 100 chars");if(await db.Rooms.AnyAsync(x=>x.WardId==p&&x.Name==n,ct))return Duplicate<RoomDto>("Room name already exists.");var e=new Room{WardId=p,Name=n};db.Add(e);try{await db.SaveChangesAsync(ct);return FacilityResult<RoomDto>.Success(new(e.Id,e.WardId,e.Name));}catch(DbUpdateException ex)when(Unique(ex)){return Duplicate<RoomDto>("Room name already exists.");}}
    public async Task<FacilityResult<RoomDto>> UpdateRoom(Guid p,Guid id,NamedInput input,CancellationToken ct){var e=await db.Rooms.FirstOrDefaultAsync(x=>x.WardId==p&&x.Id==id,ct);if(e is null)return Missing<RoomDto>();var n=Clean(input.Name);if(string.IsNullOrWhiteSpace(n)||n.Length>100)return Invalid<RoomDto>("name","required, max 100 chars");if(await db.Rooms.AnyAsync(x=>x.WardId==p&&x.Id!=id&&x.Name==n,ct))return Duplicate<RoomDto>("Room name already exists.");e.Name=n;try{await db.SaveChangesAsync(ct);return FacilityResult<RoomDto>.Success(new(e.Id,e.WardId,e.Name));}catch(DbUpdateConcurrencyException){return Concurrency<RoomDto>();}catch(DbUpdateException ex)when(Unique(ex)){return Duplicate<RoomDto>("Room name already exists.");}}
    public async Task<FacilityResult<bool>> DeleteRoom(Guid p,Guid id,CancellationToken ct){var e=await db.Rooms.FirstOrDefaultAsync(x=>x.WardId==p&&x.Id==id,ct);if(e is null)return Missing<bool>();db.Remove(e);try{await db.SaveChangesAsync(ct);return FacilityResult<bool>.Success(true);}catch(DbUpdateConcurrencyException){return Concurrency<bool>();}catch(DbUpdateException ex)when(ForeignKey(ex)){return Duplicate<bool>("Room contains beds and cannot be deleted.");}}

    public async Task<IReadOnlyList<BedDto>> ListBeds(Guid p,CancellationToken ct)=>await db.Beds.AsNoTracking().Where(x=>x.RoomId==p).OrderBy(x=>x.Number).Select(x=>new BedDto(x.Id,x.RoomId,x.Number,x.IsActive)).ToListAsync(ct);
    public async Task<FacilityResult<BedDto>> GetBed(Guid p,Guid id,CancellationToken ct){var x=await db.Beds.AsNoTracking().FirstOrDefaultAsync(x=>x.RoomId==p&&x.Id==id,ct);return x is null?Missing<BedDto>():FacilityResult<BedDto>.Success(new(x.Id,x.RoomId,x.Number,x.IsActive));}
    public async Task<FacilityResult<BedDto>> CreateBed(Guid p,BedInput input,CancellationToken ct){if(!await db.Rooms.AnyAsync(x=>x.Id==p,ct))return Missing<BedDto>();var n=Clean(input.Number);if(string.IsNullOrWhiteSpace(n)||n.Length>50)return Invalid<BedDto>("number","required, max 50 chars");if(await db.Beds.AnyAsync(x=>x.RoomId==p&&x.Number==n,ct))return Duplicate<BedDto>("Bed number already exists.");var e=new Bed{RoomId=p,Number=n,IsActive=input.IsActive};db.Add(e);try{await db.SaveChangesAsync(ct);return FacilityResult<BedDto>.Success(new(e.Id,e.RoomId,e.Number,e.IsActive));}catch(DbUpdateException ex)when(Unique(ex)){return Duplicate<BedDto>("Bed number already exists.");}}
    public async Task<FacilityResult<BedDto>> UpdateBed(Guid p,Guid id,BedInput input,CancellationToken ct){var e=await db.Beds.FirstOrDefaultAsync(x=>x.RoomId==p&&x.Id==id,ct);if(e is null)return Missing<BedDto>();var n=Clean(input.Number);if(string.IsNullOrWhiteSpace(n)||n.Length>50)return Invalid<BedDto>("number","required, max 50 chars");if(await db.Beds.AnyAsync(x=>x.RoomId==p&&x.Id!=id&&x.Number==n,ct))return Duplicate<BedDto>("Bed number already exists.");e.Number=n;e.IsActive=input.IsActive;try{await db.SaveChangesAsync(ct);return FacilityResult<BedDto>.Success(new(e.Id,e.RoomId,e.Number,e.IsActive));}catch(DbUpdateConcurrencyException){return Concurrency<BedDto>();}catch(DbUpdateException ex)when(Unique(ex)){return Duplicate<BedDto>("Bed number already exists.");}}
    public async Task<FacilityResult<bool>> DeleteBed(Guid p,Guid id,CancellationToken ct){var e=await db.Beds.FirstOrDefaultAsync(x=>x.RoomId==p&&x.Id==id,ct);if(e is null)return Missing<bool>();db.Remove(e);try{await db.SaveChangesAsync(ct);return FacilityResult<bool>.Success(true);}catch(DbUpdateConcurrencyException){return Concurrency<bool>();}}
}
