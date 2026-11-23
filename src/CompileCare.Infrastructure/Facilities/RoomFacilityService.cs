using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Facilities;

public sealed partial class FacilityService
{
    public async Task<PagedResult<RoomDto>> ListRooms(
        Guid wardId,
        PageRequest page,
        CancellationToken ct)
    {
        var query = db.Rooms
            .AsNoTracking()
            .Where(room => room.WardId == wardId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(room => room.Name)
            .ThenBy(room => room.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(room => new RoomDto(room.Id, room.WardId, room.Name))
            .ToListAsync(ct);

        return new(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<FacilityResult<RoomDto>> GetRoom(
        Guid wardId,
        Guid id,
        CancellationToken ct)
    {
        var room = await db.Rooms
            .AsNoTracking()
            .FirstOrDefaultAsync(room => room.WardId == wardId && room.Id == id, ct);

        return room is null
            ? NotFound<RoomDto>()
            : FacilityResult<RoomDto>.Success(new(room.Id, room.WardId, room.Name));
    }

    public async Task<FacilityResult<RoomDto>> CreateRoom(
        Guid wardId,
        NamedInput input,
        CancellationToken ct)
    {
        if (!await db.Wards.AnyAsync(ward => ward.Id == wardId, ct))
            return NotFound<RoomDto>();

        var name = NormalizeText(input.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return ValidationFailure<RoomDto>("name", "required, max 100 chars");

        if (await db.Rooms.AnyAsync(
                room => room.WardId == wardId && room.Name == name,
                ct))
        {
            return Conflict<RoomDto>("Room name already exists.");
        }

        var room = new Room
        {
            WardId = wardId,
            Name = name
        };

        db.Rooms.Add(room);

        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<RoomDto>.Success(new(room.Id, room.WardId, room.Name));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict<RoomDto>("Room name already exists.");
        }
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            // The parent may have been deleted after the existence check.
            return NotFound<RoomDto>();
        }
    }

    public async Task<FacilityResult<RoomDto>> UpdateRoom(
        Guid wardId,
        Guid id,
        NamedInput input,
        CancellationToken ct)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(
            room => room.WardId == wardId && room.Id == id,
            ct);

        if (room is null)
            return NotFound<RoomDto>();

        var name = NormalizeText(input.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return ValidationFailure<RoomDto>("name", "required, max 100 chars");

        if (await db.Rooms.AnyAsync(
                candidate => candidate.WardId == wardId &&
                    candidate.Id != id &&
                    candidate.Name == name,
                ct))
        {
            return Conflict<RoomDto>("Room name already exists.");
        }

        room.Name = name;

        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<RoomDto>.Success(new(room.Id, room.WardId, room.Name));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict<RoomDto>();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict<RoomDto>("Room name already exists.");
        }
    }

    public async Task<FacilityResult<bool>> DeleteRoom(
        Guid wardId,
        Guid id,
        CancellationToken ct)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(
            room => room.WardId == wardId && room.Id == id,
            ct);

        if (room is null)
            return NotFound<bool>();

        db.Rooms.Remove(room);

        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<bool>.Success(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict<bool>();
        }
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            return Conflict<bool>("Room contains beds and cannot be deleted.");
        }
    }
}
