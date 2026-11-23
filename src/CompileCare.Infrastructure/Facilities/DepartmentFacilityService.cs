using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Facilities;

public sealed partial class FacilityService
{
    public async Task<PagedResult<DepartmentDto>> ListDepartments(Guid hospitalId, PageRequest page, CancellationToken ct)
    {
        var query = db.Departments.AsNoTracking().Where(x => x.HospitalId == hospitalId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(page.Skip).Take(page.SafePageSize)
            .Select(x => new DepartmentDto(x.Id, x.HospitalId, x.Name, x.Code)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafePageSize, total);
    }
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
}
