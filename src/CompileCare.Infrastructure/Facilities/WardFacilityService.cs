using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Facilities;

public sealed partial class FacilityService
{
    public async Task<PagedResult<WardDto>> ListWards(
        Guid departmentId,
        PageRequest page,
        CancellationToken ct)
    {
        var query = db.Wards
            .AsNoTracking()
            .Where(ward => ward.DepartmentId == departmentId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(ward => ward.Name)
            .ThenBy(ward => ward.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(ward => new WardDto(ward.Id, ward.DepartmentId, ward.Name))
            .ToListAsync(ct);

        return new(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<FacilityResult<WardDto>> GetWard(
        Guid departmentId,
        Guid id,
        CancellationToken ct)
    {
        var ward = await db.Wards
            .AsNoTracking()
            .FirstOrDefaultAsync(
                ward => ward.DepartmentId == departmentId && ward.Id == id,
                ct);

        return ward is null
            ? Missing<WardDto>()
            : FacilityResult<WardDto>.Success(new(ward.Id, ward.DepartmentId, ward.Name));
    }

    public async Task<FacilityResult<WardDto>> CreateWard(
        Guid departmentId,
        NamedInput input,
        CancellationToken ct)
    {
        if (!await db.Departments.AnyAsync(department => department.Id == departmentId, ct))
            return Missing<WardDto>();

        var name = Clean(input.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Invalid<WardDto>("name", "required, max 200 chars");

        if (await db.Wards.AnyAsync(
                ward => ward.DepartmentId == departmentId && ward.Name == name,
                ct))
        {
            return Duplicate<WardDto>("Ward name already exists.");
        }

        var ward = new Ward
        {
            DepartmentId = departmentId,
            Name = name
        };

        db.Wards.Add(ward);

        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<WardDto>.Success(new(ward.Id, ward.DepartmentId, ward.Name));
        }
        catch (DbUpdateException exception) when (Unique(exception))
        {
            return Duplicate<WardDto>("Ward name already exists.");
        }
        catch (DbUpdateException exception) when (ForeignKey(exception))
        {
            // The parent may have been deleted after the existence check.
            return Missing<WardDto>();
        }
    }

    public async Task<FacilityResult<WardDto>> UpdateWard(
        Guid departmentId,
        Guid id,
        NamedInput input,
        CancellationToken ct)
    {
        var ward = await db.Wards.FirstOrDefaultAsync(
            ward => ward.DepartmentId == departmentId && ward.Id == id,
            ct);

        if (ward is null)
            return Missing<WardDto>();

        var name = Clean(input.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Invalid<WardDto>("name", "required, max 200 chars");

        if (await db.Wards.AnyAsync(
                candidate => candidate.DepartmentId == departmentId &&
                    candidate.Id != id &&
                    candidate.Name == name,
                ct))
        {
            return Duplicate<WardDto>("Ward name already exists.");
        }

        ward.Name = name;

        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<WardDto>.Success(new(ward.Id, ward.DepartmentId, ward.Name));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Concurrency<WardDto>();
        }
        catch (DbUpdateException exception) when (Unique(exception))
        {
            return Duplicate<WardDto>("Ward name already exists.");
        }
    }

    public async Task<FacilityResult<bool>> DeleteWard(
        Guid departmentId,
        Guid id,
        CancellationToken ct)
    {
        var ward = await db.Wards.FirstOrDefaultAsync(
            ward => ward.DepartmentId == departmentId && ward.Id == id,
            ct);

        if (ward is null)
            return Missing<bool>();

        db.Wards.Remove(ward);

        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<bool>.Success(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Concurrency<bool>();
        }
        catch (DbUpdateException exception) when (ForeignKey(exception))
        {
            return Duplicate<bool>("Ward contains rooms and cannot be deleted.");
        }
    }
}
