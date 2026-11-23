using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Facilities;

public sealed partial class FacilityService
{
    public async Task<PagedResult<HospitalDto>> ListHospitals(PageRequest page, CancellationToken ct)
    {
        var query = db.Hospitals.AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(page.Skip).Take(page.SafePageSize)
            .Select(x => new HospitalDto(x.Id, x.Name, x.Code)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<FacilityResult<HospitalDto>> GetHospital(Guid id, CancellationToken ct)
    {
        var x = await db.Hospitals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return x is null ? NotFound<HospitalDto>() : FacilityResult<HospitalDto>.Success(new(x.Id, x.Name, x.Code));
    }

    public async Task<FacilityResult<HospitalDto>> CreateHospital(HospitalInput input, CancellationToken ct)
    {
        if (db.CurrentTenantId is null || db.CurrentTenantId == Guid.Empty)
            return FacilityResult<HospitalDto>.Failure("Tenant context required.", 400);
        var name = NormalizeName(input.Name); var code = NormalizeCode(input.Code);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return ValidationFailure<HospitalDto>("name", "required, max 200 chars");
        if (code?.Length > 50) return ValidationFailure<HospitalDto>("code", "max 50 chars");
        if (await db.Hospitals.AnyAsync(x => x.Name == name || (code != null && x.Code == code), ct))
            return Conflict<HospitalDto>("Hospital name/code already exists for this tenant.");
        var entity = new Hospital { Name = name, Code = code };
        db.Hospitals.Add(entity);
        try { await db.SaveChangesAsync(ct); return FacilityResult<HospitalDto>.Success(new(entity.Id, entity.Name, entity.Code)); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Conflict<HospitalDto>("Hospital name/code already exists for this tenant."); }
    }

    public async Task<FacilityResult<HospitalDto>> UpdateHospital(Guid id, HospitalInput input, CancellationToken ct)
    {
        var entity = await db.Hospitals.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound<HospitalDto>();
        var name = NormalizeName(input.Name); var code = NormalizeCode(input.Code);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return ValidationFailure<HospitalDto>("name", "required, max 200 chars");
        if (code?.Length > 50) return ValidationFailure<HospitalDto>("code", "max 50 chars");
        if (await db.Hospitals.AnyAsync(x => x.Id != id && (x.Name == name || (code != null && x.Code == code)), ct))
            return Conflict<HospitalDto>("Hospital name/code already exists for this tenant.");
        entity.Name = name; entity.Code = code;
        try { await db.SaveChangesAsync(ct); return FacilityResult<HospitalDto>.Success(new(entity.Id, entity.Name, entity.Code)); }
        catch (DbUpdateConcurrencyException) { return ConcurrencyConflict<HospitalDto>(); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Conflict<HospitalDto>("Hospital name/code already exists for this tenant."); }
    }

    public async Task<FacilityResult<bool>> DeleteHospital(Guid id, CancellationToken ct)
    {
        var entity = await db.Hospitals.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound<bool>();
        db.Hospitals.Remove(entity);
        try { await db.SaveChangesAsync(ct); return FacilityResult<bool>.Success(true); }
        catch (DbUpdateConcurrencyException) { return ConcurrencyConflict<bool>(); }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex)) { return Conflict<bool>("Hospital contains departments and cannot be deleted."); }
    }
}
