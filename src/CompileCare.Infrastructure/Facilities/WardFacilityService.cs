using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Facilities;

public sealed partial class FacilityService
{
    public async Task<PagedResult<WardDto>> ListWards(Guid p, PageRequest page, CancellationToken ct)
    {
        var query = db.Wards.AsNoTracking().Where(x => x.DepartmentId == p);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(page.Skip).Take(page.SafePageSize)
            .Select(x => new WardDto(x.Id, x.DepartmentId, x.Name)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafePageSize, total);
    }
    public async Task<FacilityResult<WardDto>> GetWard(Guid p,Guid id,CancellationToken ct){var x=await db.Wards.AsNoTracking().FirstOrDefaultAsync(x=>x.DepartmentId==p&&x.Id==id,ct);return x is null?Missing<WardDto>():FacilityResult<WardDto>.Success(new(x.Id,x.DepartmentId,x.Name));}
    public async Task<FacilityResult<WardDto>> CreateWard(Guid p,NamedInput input,CancellationToken ct){if(!await db.Departments.AnyAsync(x=>x.Id==p,ct))return Missing<WardDto>();var n=Clean(input.Name);if(string.IsNullOrWhiteSpace(n)||n.Length>200)return Invalid<WardDto>("name","required, max 200 chars");if(await db.Wards.AnyAsync(x=>x.DepartmentId==p&&x.Name==n,ct))return Duplicate<WardDto>("Ward name already exists.");var e=new Ward{DepartmentId=p,Name=n};db.Add(e);try{await db.SaveChangesAsync(ct);return FacilityResult<WardDto>.Success(new(e.Id,e.DepartmentId,e.Name));}catch(DbUpdateException ex)when(Unique(ex)){return Duplicate<WardDto>("Ward name already exists.");}}
    public async Task<FacilityResult<WardDto>> UpdateWard(Guid p,Guid id,NamedInput input,CancellationToken ct){var e=await db.Wards.FirstOrDefaultAsync(x=>x.DepartmentId==p&&x.Id==id,ct);if(e is null)return Missing<WardDto>();var n=Clean(input.Name);if(string.IsNullOrWhiteSpace(n)||n.Length>200)return Invalid<WardDto>("name","required, max 200 chars");if(await db.Wards.AnyAsync(x=>x.DepartmentId==p&&x.Id!=id&&x.Name==n,ct))return Duplicate<WardDto>("Ward name already exists.");e.Name=n;try{await db.SaveChangesAsync(ct);return FacilityResult<WardDto>.Success(new(e.Id,e.DepartmentId,e.Name));}catch(DbUpdateConcurrencyException){return Concurrency<WardDto>();}catch(DbUpdateException ex)when(Unique(ex)){return Duplicate<WardDto>("Ward name already exists.");}}
    public async Task<FacilityResult<bool>> DeleteWard(Guid p,Guid id,CancellationToken ct){var e=await db.Wards.FirstOrDefaultAsync(x=>x.DepartmentId==p&&x.Id==id,ct);if(e is null)return Missing<bool>();db.Remove(e);try{await db.SaveChangesAsync(ct);return FacilityResult<bool>.Success(true);}catch(DbUpdateConcurrencyException){return Concurrency<bool>();}catch(DbUpdateException ex)when(ForeignKey(ex)){return Duplicate<bool>("Ward contains rooms and cannot be deleted.");}}
}
