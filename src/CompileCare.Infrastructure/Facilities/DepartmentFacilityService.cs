using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Facilities;

public sealed partial class FacilityService
{
    public async Task<PagedResult<DepartmentDto>> ListDepartments(
        Guid hospitalId,
        PageRequest page,
        CancellationToken ct)
    {
        var query = db.Departments
            .AsNoTracking()
            .Where(department => department.HospitalId == hospitalId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(department => department.Name)
            .ThenBy(department => department.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(department => new DepartmentDto(
                department.Id,
                department.HospitalId,
                department.Name,
                department.Code))
            .ToListAsync(ct);

        return new(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<FacilityResult<DepartmentDto>> GetDepartment(
        Guid hospitalId,
        Guid id,
        CancellationToken ct)
    {
        var department = await db.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                department => department.HospitalId == hospitalId && department.Id == id,
                ct);

        return department is null
            ? Missing<DepartmentDto>()
            : FacilityResult<DepartmentDto>.Success(new(
                department.Id,
                department.HospitalId,
                department.Name,
                department.Code));
    }

    public async Task<FacilityResult<DepartmentDto>> CreateDepartment(
        Guid hospitalId,
        DepartmentInput input,
        CancellationToken ct)
    {
        if (!await db.Hospitals.AnyAsync(hospital => hospital.Id == hospitalId, ct))
            return Missing<DepartmentDto>();

        var name = Clean(input.Name);
        var code = CleanCode(input.Code);

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Invalid<DepartmentDto>("name", "required, max 200 chars");

        if (code?.Length > 50)
            return Invalid<DepartmentDto>("code", "max 50 chars");

        if (await db.Departments.AnyAsync(
                department => department.HospitalId == hospitalId &&
                    (department.Name == name || (code != null && department.Code == code)),
                ct))
        {
            return Duplicate<DepartmentDto>("Department name/code already exists.");
        }

        var department = new Department
        {
            HospitalId = hospitalId,
            Name = name,
            Code = code
        };

        db.Departments.Add(department);

        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<DepartmentDto>.Success(new(
                department.Id,
                department.HospitalId,
                department.Name,
                department.Code));
        }
        catch (DbUpdateException exception) when (Unique(exception))
        {
            return Duplicate<DepartmentDto>("Department name/code already exists.");
        }
        catch (DbUpdateException exception) when (ForeignKey(exception))
        {
            // The parent may have been deleted after the existence check.
            return Missing<DepartmentDto>();
        }
    }

    public async Task<FacilityResult<DepartmentDto>> UpdateDepartment(
        Guid hospitalId,
        Guid id,
        DepartmentInput input,
        CancellationToken ct)
    {
        var department = await db.Departments.FirstOrDefaultAsync(
            department => department.HospitalId == hospitalId && department.Id == id,
            ct);

        if (department is null)
            return Missing<DepartmentDto>();

        var name = Clean(input.Name);
        var code = CleanCode(input.Code);

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Invalid<DepartmentDto>("name", "required, max 200 chars");

        if (code?.Length > 50)
            return Invalid<DepartmentDto>("code", "max 50 chars");

        if (await db.Departments.AnyAsync(
                candidate => candidate.HospitalId == hospitalId &&
                    candidate.Id != id &&
                    (candidate.Name == name || (code != null && candidate.Code == code)),
                ct))
        {
            return Duplicate<DepartmentDto>("Department name/code already exists.");
        }

        department.Name = name;
        department.Code = code;

        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<DepartmentDto>.Success(new(
                department.Id,
                department.HospitalId,
                department.Name,
                department.Code));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Concurrency<DepartmentDto>();
        }
        catch (DbUpdateException exception) when (Unique(exception))
        {
            return Duplicate<DepartmentDto>("Department name/code already exists.");
        }
    }

    public async Task<FacilityResult<bool>> DeleteDepartment(
        Guid hospitalId,
        Guid id,
        CancellationToken ct)
    {
        var department = await db.Departments.FirstOrDefaultAsync(
            department => department.HospitalId == hospitalId && department.Id == id,
            ct);

        if (department is null)
            return Missing<bool>();

        db.Departments.Remove(department);

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
            return Duplicate<bool>("Department contains wards and cannot be deleted.");
        }
    }
}
