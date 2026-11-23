using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Facilities;

public sealed partial class FacilityService
{
    public async Task<PagedResult<RoomDto>> ListRooms(Guid p, PageRequest page, CancellationToken ct)
    {
        var query = db.Rooms.AsNoTracking().Where(x => x.WardId == p);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(page.Skip).Take(page.SafePageSize)
            .Select(x => new RoomDto(x.Id, x.WardId, x.Name)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafePageSize, total);
    }
    public async Task<FacilityResult<RoomDto>> GetRoom(Guid p,Guid id,CancellationToken ct){var x=await db.Rooms.AsNoTracking().FirstOrDefaultAsync(x=>x.WardId==p&&x.Id==id,ct);return x is null?Missing<RoomDto>():FacilityResult<RoomDto>.Success(new(x.Id,x.WardId,x.Name));}
    public async Task<FacilityResult<RoomDto>> CreateRoom(Guid p,NamedInput input,CancellationToken ct){if(!await db.Wards.AnyAsync(x=>x.Id==p,ct))return Missing<RoomDto>();var n=Clean(input.Name);if(string.IsNullOrWhiteSpace(n)||n.Length>100)return Invalid<RoomDto>("name","required, max 100 chars");if(await db.Rooms.AnyAsync(x=>x.WardId==p&&x.Name==n,ct))return Duplicate<RoomDto>("Room name already exists.");var e=new Room{WardId=p,Name=n};db.Add(e);try{await db.SaveChangesAsync(ct);return FacilityResult<RoomDto>.Success(new(e.Id,e.WardId,e.Name));}catch(DbUpdateException ex)when(Unique(ex)){return Duplicate<RoomDto>("Room name already exists.");}}
    public async Task<FacilityResult<RoomDto>> UpdateRoom(Guid p,Guid id,NamedInput input,CancellationToken ct){var e=await db.Rooms.FirstOrDefaultAsync(x=>x.WardId==p&&x.Id==id,ct);if(e is null)return Missing<RoomDto>();var n=Clean(input.Name);if(string.IsNullOrWhiteSpace(n)||n.Length>100)return Invalid<RoomDto>("name","required, max 100 chars");if(await db.Rooms.AnyAsync(x=>x.WardId==p&&x.Id!=id&&x.Name==n,ct))return Duplicate<RoomDto>("Room name already exists.");e.Name=n;try{await db.SaveChangesAsync(ct);return FacilityResult<RoomDto>.Success(new(e.Id,e.WardId,e.Name));}catch(DbUpdateConcurrencyException){return Concurrency<RoomDto>();}catch(DbUpdateException ex)when(Unique(ex)){return Duplicate<RoomDto>("Room name already exists.");}}
    public async Task<FacilityResult<bool>> DeleteRoom(Guid p,Guid id,CancellationToken ct){var e=await db.Rooms.FirstOrDefaultAsync(x=>x.WardId==p&&x.Id==id,ct);if(e is null)return Missing<bool>();db.Remove(e);try{await db.SaveChangesAsync(ct);return FacilityResult<bool>.Success(true);}catch(DbUpdateConcurrencyException){return Concurrency<bool>();}catch(DbUpdateException ex)when(ForeignKey(ex)){return Duplicate<bool>("Room contains beds and cannot be deleted.");}}
}
