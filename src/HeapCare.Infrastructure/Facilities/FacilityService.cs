using HeapCare.Application.Facilities;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HeapCare.Infrastructure.Facilities;

public sealed partial class FacilityService(
    HeapCareDbContext db,
    IOrganizationScopeContext organizationScopeContext) : IFacilityService
{
    private IQueryable<Hospital> ScopedHospitals()
    {
        if (organizationScopeContext.IsTenantWide)
            return db.Hospitals;

        var hospitalIds = organizationScopeContext.HospitalIds.ToArray();
        var departmentIds = organizationScopeContext.DepartmentIds.ToArray();
        return db.Hospitals.Where(hospital =>
            hospitalIds.Contains(hospital.Id) ||
            db.Departments.Any(department =>
                department.HospitalId == hospital.Id && departmentIds.Contains(department.Id)));
    }

    private IQueryable<Department> ScopedDepartments()
    {
        if (organizationScopeContext.IsTenantWide)
            return db.Departments;

        var hospitalIds = organizationScopeContext.HospitalIds.ToArray();
        var departmentIds = organizationScopeContext.DepartmentIds.ToArray();
        return db.Departments.Where(department =>
            hospitalIds.Contains(department.HospitalId) || departmentIds.Contains(department.Id));
    }

    private IQueryable<Ward> ScopedWards()
    {
        if (organizationScopeContext.IsTenantWide)
            return db.Wards;

        var hospitalIds = organizationScopeContext.HospitalIds.ToArray();
        var departmentIds = organizationScopeContext.DepartmentIds.ToArray();
        return db.Wards.Where(ward =>
            departmentIds.Contains(ward.DepartmentId) ||
            db.Departments.Any(department =>
                department.Id == ward.DepartmentId && hospitalIds.Contains(department.HospitalId)));
    }

    private IQueryable<Room> ScopedRooms()
    {
        if (organizationScopeContext.IsTenantWide)
            return db.Rooms;

        var hospitalIds = organizationScopeContext.HospitalIds.ToArray();
        var departmentIds = organizationScopeContext.DepartmentIds.ToArray();
        return db.Rooms.Where(room =>
            db.Wards.Any(ward =>
                ward.Id == room.WardId &&
                (departmentIds.Contains(ward.DepartmentId) ||
                 db.Departments.Any(department =>
                     department.Id == ward.DepartmentId && hospitalIds.Contains(department.HospitalId)))));
    }

    private IQueryable<Bed> ScopedBeds()
    {
        if (organizationScopeContext.IsTenantWide)
            return db.Beds;

        var hospitalIds = organizationScopeContext.HospitalIds.ToArray();
        var departmentIds = organizationScopeContext.DepartmentIds.ToArray();
        return db.Beds.Where(bed =>
            db.Rooms.Any(room =>
                room.Id == bed.RoomId &&
                db.Wards.Any(ward =>
                    ward.Id == room.WardId &&
                    (departmentIds.Contains(ward.DepartmentId) ||
                     db.Departments.Any(department =>
                         department.Id == ward.DepartmentId && hospitalIds.Contains(department.HospitalId))))));
    }

    private static FacilityResult<T> NotFound<T>() =>
        FacilityResult<T>.Failure("Resource not found.", 404);

    private static FacilityResult<T> ValidationFailure<T>(string field, string message) =>
        FacilityResult<T>.Failure($"{field}: {message}", 400);

    private static FacilityResult<T> Conflict<T>(string message) =>
        FacilityResult<T>.Failure(message, 409);

    private static FacilityResult<T> ConcurrencyConflict<T>() =>
        FacilityResult<T>.Failure("Resource changed concurrently. Reload and retry.", 409);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };

    private static string? NormalizeText(string? value) => value?.Trim();
    private static string? NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
