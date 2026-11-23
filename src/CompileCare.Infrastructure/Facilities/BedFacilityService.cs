using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Facilities;

public sealed partial class FacilityService
{
    public async Task<PagedResult<BedDto>> ListBeds(Guid p, PageRequest page, CancellationToken ct)
    {
        var query = db.Beds.AsNoTracking().Where(x => x.RoomId == p);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Number).ThenBy(x => x.Id).Skip(page.Skip).Take(page.SafePageSize)
            .Select(x => new BedDto(x.Id, x.RoomId, x.Number, x.IsActive)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafePageSize, total);
    }
    public async Task<FacilityResult<BedDto>> GetBed(Guid p,Guid id,CancellationToken ct){var x=await db.Beds.AsNoTracking().FirstOrDefaultAsync(x=>x.RoomId==p&&x.Id==id,ct);return x is null?Missing<BedDto>():FacilityResult<BedDto>.Success(new(x.Id,x.RoomId,x.Number,x.IsActive));}
    public async Task<FacilityResult<BedDto>> CreateBed(Guid p,BedInput input,CancellationToken ct){if(!await db.Rooms.AnyAsync(x=>x.Id==p,ct))return Missing<BedDto>();var n=Clean(input.Number);if(string.IsNullOrWhiteSpace(n)||n.Length>50)return Invalid<BedDto>("number","required, max 50 chars");if(await db.Beds.AnyAsync(x=>x.RoomId==p&&x.Number==n,ct))return Duplicate<BedDto>("Bed number already exists.");var e=new Bed{RoomId=p,Number=n,IsActive=input.IsActive};db.Add(e);try{await db.SaveChangesAsync(ct);return FacilityResult<BedDto>.Success(new(e.Id,e.RoomId,e.Number,e.IsActive));}catch(DbUpdateException ex)when(Unique(ex)){return Duplicate<BedDto>("Bed number already exists.");}}
    public async Task<FacilityResult<BedDto>> UpdateBed(Guid p,Guid id,BedInput input,CancellationToken ct){var e=await db.Beds.FirstOrDefaultAsync(x=>x.RoomId==p&&x.Id==id,ct);if(e is null)return Missing<BedDto>();var n=Clean(input.Number);if(string.IsNullOrWhiteSpace(n)||n.Length>50)return Invalid<BedDto>("number","required, max 50 chars");if(await db.Beds.AnyAsync(x=>x.RoomId==p&&x.Id!=id&&x.Number==n,ct))return Duplicate<BedDto>("Bed number already exists.");e.Number=n;e.IsActive=input.IsActive;try{await db.SaveChangesAsync(ct);return FacilityResult<BedDto>.Success(new(e.Id,e.RoomId,e.Number,e.IsActive));}catch(DbUpdateConcurrencyException){return Concurrency<BedDto>();}catch(DbUpdateException ex)when(Unique(ex)){return Duplicate<BedDto>("Bed number already exists.");}}
    public async Task<FacilityResult<bool>> DeleteBed(Guid p, Guid id, CancellationToken ct)
    {
        var entity = await db.Beds.FirstOrDefaultAsync(x => x.RoomId == p && x.Id == id, ct);
        if (entity is null) return Missing<bool>();
        db.Remove(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<bool>.Success(true);
        }
        catch (DbUpdateConcurrencyException) { return Concurrency<bool>(); }
        catch (DbUpdateException ex) when (ForeignKey(ex)) { return Duplicate<bool>("Bed cannot be deleted while referenced by another resource."); }
    }
}
