using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Facilities;

public sealed partial class FacilityService
{
    public async Task<PagedResult<BedDto>> ListBeds(
        Guid roomId,
        PageRequest page,
        CancellationToken ct)
    {
        var query = db.Beds
            .AsNoTracking()
            .Where(bed => bed.RoomId == roomId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(bed => bed.Number)
            .ThenBy(bed => bed.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(bed => new BedDto(bed.Id, bed.RoomId, bed.Number, bed.IsActive))
            .ToListAsync(ct);

        return new(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<FacilityResult<BedDto>> GetBed(
        Guid roomId,
        Guid id,
        CancellationToken ct)
    {
        var bed = await db.Beds
            .AsNoTracking()
            .FirstOrDefaultAsync(bed => bed.RoomId == roomId && bed.Id == id, ct);

        return bed is null
            ? NotFound<BedDto>()
            : FacilityResult<BedDto>.Success(new(bed.Id, bed.RoomId, bed.Number, bed.IsActive));
    }

    public async Task<FacilityResult<BedDto>> CreateBed(
        Guid roomId,
        BedInput input,
        CancellationToken ct)
    {
        if (!await db.Rooms.AnyAsync(room => room.Id == roomId, ct))
            return NotFound<BedDto>();

        var number = NormalizeName(input.Number);
        if (string.IsNullOrWhiteSpace(number) || number.Length > 50)
            return ValidationFailure<BedDto>("number", "required, max 50 chars");

        if (await db.Beds.AnyAsync(
                bed => bed.RoomId == roomId && bed.Number == number,
                ct))
        {
            return Conflict<BedDto>("Bed number already exists.");
        }

        var bed = new Bed
        {
            RoomId = roomId,
            Number = number,
            IsActive = input.IsActive
        };

        db.Beds.Add(bed);

        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<BedDto>.Success(new(bed.Id, bed.RoomId, bed.Number, bed.IsActive));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict<BedDto>("Bed number already exists.");
        }
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            // The parent may have been deleted after the existence check.
            return NotFound<BedDto>();
        }
    }

    public async Task<FacilityResult<BedDto>> UpdateBed(
        Guid roomId,
        Guid id,
        BedInput input,
        CancellationToken ct)
    {
        var bed = await db.Beds.FirstOrDefaultAsync(
            bed => bed.RoomId == roomId && bed.Id == id,
            ct);

        if (bed is null)
            return NotFound<BedDto>();

        var number = NormalizeName(input.Number);
        if (string.IsNullOrWhiteSpace(number) || number.Length > 50)
            return ValidationFailure<BedDto>("number", "required, max 50 chars");

        if (await db.Beds.AnyAsync(
                candidate => candidate.RoomId == roomId &&
                    candidate.Id != id &&
                    candidate.Number == number,
                ct))
        {
            return Conflict<BedDto>("Bed number already exists.");
        }

        bed.Number = number;
        bed.IsActive = input.IsActive;

        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<BedDto>.Success(new(bed.Id, bed.RoomId, bed.Number, bed.IsActive));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict<BedDto>();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict<BedDto>("Bed number already exists.");
        }
    }

    public async Task<FacilityResult<bool>> DeleteBed(
        Guid roomId,
        Guid id,
        CancellationToken ct)
    {
        var bed = await db.Beds.FirstOrDefaultAsync(
            bed => bed.RoomId == roomId && bed.Id == id,
            ct);

        if (bed is null)
            return NotFound<bool>();

        db.Beds.Remove(bed);

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
            return Conflict<bool>("Bed cannot be deleted while referenced by another resource.");
        }
    }
}
