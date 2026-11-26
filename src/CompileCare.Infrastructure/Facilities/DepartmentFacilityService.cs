using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Facilities;

public sealed partial class FacilityService
{
    public async Task<FacilityResult<PagedResult<DepartmentDto>>> ListDepartments(
        Guid hospitalId,
        PageRequest page,
        CancellationToken ct)
    {
        if (!await ScopedHospitals().AnyAsync(hospital => hospital.Id == hospitalId, ct))
            return NotFound<PagedResult<DepartmentDto>>();

        var query = ScopedDepartments()
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

        return FacilityResult<PagedResult<DepartmentDto>>.Success(
            new(items, page.SafePage, page.SafePageSize, total));
    }

    public async Task<FacilityResult<DepartmentDto>> GetDepartment(
        Guid hospitalId,
        Guid id,
        CancellationToken ct)
    {
        var department = await ScopedDepartments()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                department => department.HospitalId == hospitalId && department.Id == id,
                ct);

        return department is null
            ? NotFound<DepartmentDto>()
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
        if (!organizationScopeContext.CanManageHospital(hospitalId))
            return NotFound<DepartmentDto>();

        if (!await ScopedHospitals().AnyAsync(hospital => hospital.Id == hospitalId, ct))
            return NotFound<DepartmentDto>();

        var name = NormalizeText(input.Name);
        var code = NormalizeCode(input.Code);

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return ValidationFailure<DepartmentDto>("name", "required, max 200 chars");

        if (code?.Length > 50)
            return ValidationFailure<DepartmentDto>("code", "max 50 chars");

        if (await ScopedDepartments().AnyAsync(
                department => department.HospitalId == hospitalId &&
                    (department.Name == name || (code != null && department.Code == code)),
                ct))
        {
            return Conflict<DepartmentDto>("Department name/code already exists.");
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
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict<DepartmentDto>("Department name/code already exists.");
        }
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            // The parent may have been deleted after the existence check.
            return NotFound<DepartmentDto>();
        }
    }

    public async Task<FacilityResult<DepartmentDto>> UpdateDepartment(
        Guid hospitalId,
        Guid id,
        DepartmentInput input,
        CancellationToken ct)
    {
        var department = await ScopedDepartments().FirstOrDefaultAsync(
            department => department.HospitalId == hospitalId && department.Id == id,
            ct);

        if (department is null)
            return NotFound<DepartmentDto>();

        var name = NormalizeText(input.Name);
        var code = NormalizeCode(input.Code);

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return ValidationFailure<DepartmentDto>("name", "required, max 200 chars");

        if (code?.Length > 50)
            return ValidationFailure<DepartmentDto>("code", "max 50 chars");

        if (await ScopedDepartments().AnyAsync(
                candidate => candidate.HospitalId == hospitalId &&
                    candidate.Id != id &&
                    (candidate.Name == name || (code != null && candidate.Code == code)),
                ct))
        {
            return Conflict<DepartmentDto>("Department name/code already exists.");
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
            return ConcurrencyConflict<DepartmentDto>();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict<DepartmentDto>("Department name/code already exists.");
        }
    }

    public async Task<FacilityResult<bool>> DeleteDepartment(
        Guid hospitalId,
        Guid id,
        CancellationToken ct)
    {
        var department = await ScopedDepartments().FirstOrDefaultAsync(
            department => department.HospitalId == hospitalId && department.Id == id,
            ct);

        if (department is null)
            return NotFound<bool>();

        db.Departments.Remove(department);

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
            return Conflict<bool>("Department contains wards and cannot be deleted.");
        }
    }
}
