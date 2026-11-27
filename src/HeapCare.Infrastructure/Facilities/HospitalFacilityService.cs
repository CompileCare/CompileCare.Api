using HeapCare.Application.Facilities;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HeapCare.Infrastructure.Facilities;

public sealed partial class FacilityService
{
    public async Task<PagedResult<HospitalDto>> ListHospitals(
        PageRequest page,
        CancellationToken ct)
    {
        var query = ScopedHospitals().AsNoTracking();
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(hospital => hospital.Name)
            .ThenBy(hospital => hospital.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(hospital => new HospitalDto(
                hospital.Id,
                hospital.Name,
                hospital.Code))
            .ToListAsync(ct);

        return new(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<FacilityResult<HospitalDto>> GetHospital(
        Guid id,
        CancellationToken ct)
    {
        var hospital = await ScopedHospitals()
            .AsNoTracking()
            .FirstOrDefaultAsync(hospital => hospital.Id == id, ct);

        return hospital is null
            ? NotFound<HospitalDto>()
            : FacilityResult<HospitalDto>.Success(new(
                hospital.Id,
                hospital.Name,
                hospital.Code));
    }

    public async Task<FacilityResult<HospitalDto>> CreateHospital(
        HospitalInput input,
        CancellationToken ct)
    {
        if (db.CurrentTenantId is null || db.CurrentTenantId == Guid.Empty)
            return FacilityResult<HospitalDto>.Failure("Tenant context required.", 400);
        if (!organizationScopeContext.IsTenantWide)
            return FacilityResult<HospitalDto>.Failure("A tenant-wide organization scope is required to create a hospital.", 403);

        var name = NormalizeText(input.Name);
        var code = NormalizeCode(input.Code);

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return ValidationFailure<HospitalDto>("name", "required, max 200 chars");

        if (code?.Length > 50)
            return ValidationFailure<HospitalDto>("code", "max 50 chars");

        if (await ScopedHospitals().AnyAsync(
                hospital => hospital.Name == name ||
                    (code != null && hospital.Code == code),
                ct))
        {
            return Conflict<HospitalDto>(
                "Hospital name/code already exists for this tenant.");
        }

        var hospital = new Hospital
        {
            Name = name,
            Code = code
        };

        db.Hospitals.Add(hospital);

        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<HospitalDto>.Success(new(
                hospital.Id,
                hospital.Name,
                hospital.Code));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict<HospitalDto>(
                "Hospital name/code already exists for this tenant.");
        }
    }

    public async Task<FacilityResult<HospitalDto>> UpdateHospital(
        Guid id,
        HospitalInput input,
        CancellationToken ct)
    {
        if (!organizationScopeContext.CanManageHospital(id))
            return NotFound<HospitalDto>();

        var hospital = await ScopedHospitals().FirstOrDefaultAsync(
            hospital => hospital.Id == id,
            ct);

        if (hospital is null)
            return NotFound<HospitalDto>();

        var name = NormalizeText(input.Name);
        var code = NormalizeCode(input.Code);

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return ValidationFailure<HospitalDto>("name", "required, max 200 chars");

        if (code?.Length > 50)
            return ValidationFailure<HospitalDto>("code", "max 50 chars");

        if (await ScopedHospitals().AnyAsync(
                candidate => candidate.Id != id &&
                    (candidate.Name == name || (code != null && candidate.Code == code)),
                ct))
        {
            return Conflict<HospitalDto>(
                "Hospital name/code already exists for this tenant.");
        }

        hospital.Name = name;
        hospital.Code = code;

        try
        {
            await db.SaveChangesAsync(ct);
            return FacilityResult<HospitalDto>.Success(new(
                hospital.Id,
                hospital.Name,
                hospital.Code));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict<HospitalDto>();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict<HospitalDto>(
                "Hospital name/code already exists for this tenant.");
        }
    }

    public async Task<FacilityResult<bool>> DeleteHospital(
        Guid id,
        CancellationToken ct)
    {
        if (!organizationScopeContext.CanManageHospital(id))
            return NotFound<bool>();

        var hospital = await ScopedHospitals().FirstOrDefaultAsync(
            hospital => hospital.Id == id,
            ct);

        if (hospital is null)
            return NotFound<bool>();

        db.Hospitals.Remove(hospital);

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
            return Conflict<bool>(
                "Hospital contains departments and cannot be deleted.");
        }
    }
}
