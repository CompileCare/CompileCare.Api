using CompileCare.Application.Staff;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Staff;

public sealed class StaffManagementService(
    CompileCareDbContext db,
    IOrganizationScopeContext organizationScope) : IStaffManagementService
{
    public async Task<StaffResult<StaffProfileDto>> CreateAsync(
        StaffProfileCreateInput input, CancellationToken cancellationToken)
    {
        var staffCode = NormalizeCode(input.StaffCode);
        var givenName = Normalize(input.GivenName);
        var middleName = Normalize(input.MiddleName);
        var familyName = Normalize(input.FamilyName);
        var designation = Normalize(input.ProviderDesignation);

        if (staffCode is null || staffCode.Length > 40 || givenName is null || givenName.Length > 100)
            return StaffResult<StaffProfileDto>.Failure("Staff code and given name are required and must be within their length limits.", 400);
        if (middleName?.Length > 100 || familyName?.Length > 100 || designation?.Length > 100)
            return StaffResult<StaffProfileDto>.Failure("Name and provider designation fields must be at most 100 characters.", 400);
        if (!await CanManageAssignmentAsync(input.HospitalId, input.DepartmentId, cancellationToken))
            return StaffResult<StaffProfileDto>.Failure("Hospital or department not found.", 404);

        if (await db.StaffProfiles.AnyAsync(x => x.StaffCode == staffCode, cancellationToken))
            return StaffResult<StaffProfileDto>.Failure("A staff profile with this staff code already exists in the tenant.", 409);

        var effectiveFrom = input.EffectiveFrom ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var profile = new StaffProfile
        {
            StaffCode = staffCode,
            GivenName = givenName,
            MiddleName = middleName,
            FamilyName = familyName,
            ProviderDesignation = designation
        };
        var assignment = new StaffAssignment
        {
            StaffProfileId = profile.Id,
            HospitalId = input.HospitalId,
            DepartmentId = input.DepartmentId,
            EffectiveFrom = effectiveFrom
        };
        db.StaffProfiles.Add(profile);
        db.StaffAssignments.Add(assignment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: "23505" })
        {
            return StaffResult<StaffProfileDto>.Failure("A staff profile with this staff code already exists in the tenant.", 409);
        }
        return StaffResult<StaffProfileDto>.Success(ToDto(profile, [assignment], canManageWithinScope: true));
    }

    public async Task<StaffResult<StaffProfileDto>> GetAsync(Guid staffId, CancellationToken cancellationToken)
    {
        var profile = await ReadableProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == staffId, cancellationToken);
        if (profile is null)
            return StaffResult<StaffProfileDto>.Failure("Staff profile not found.", 404);
        var allAssignments = await db.StaffAssignments.AsNoTracking()
            .Where(x => x.StaffProfileId == staffId)
            .OrderBy(x => x.EffectiveFrom)
            .ToListAsync(cancellationToken);
        var readableAssignments = allAssignments.Where(IsReadableAssignment).ToArray();
        return StaffResult<StaffProfileDto>.Success(
            ToDto(profile, readableAssignments, CanManageWithinScope(allAssignments)));
    }

    public async Task<StaffResult<StaffPage>> ListAsync(StaffSearchRequest request, CancellationToken cancellationToken)
    {
        var query = ReadableProfiles().AsNoTracking();
        var term = Normalize(request.Query);
        if (term is not null)
        {
            var pattern = $"%{EscapeLike(term)}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.StaffCode, pattern) ||
                EF.Functions.ILike(x.GivenName, pattern) ||
                (x.MiddleName != null && EF.Functions.ILike(x.MiddleName, pattern)) ||
                (x.FamilyName != null && EF.Functions.ILike(x.FamilyName, pattern)));
        }

        var total = await query.CountAsync(cancellationToken);
        var page = request.SafePage;
        var pageSize = request.SafePageSize;
        var profiles = await query.OrderBy(x => x.StaffCode).ThenBy(x => x.Id)
            .Skip(request.Skip).Take(pageSize).ToListAsync(cancellationToken);
        var ids = profiles.Select(x => x.Id).ToArray();
        var allAssignments = await db.StaffAssignments.AsNoTracking()
            .Where(x => ids.Contains(x.StaffProfileId))
            .OrderBy(x => x.EffectiveFrom)
            .ToListAsync(cancellationToken);
        var assignmentMap = allAssignments.Where(IsReadableAssignment)
            .GroupBy(x => x.StaffProfileId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<StaffAssignment>)x.ToArray());
        var manageabilityMap = allAssignments.GroupBy(x => x.StaffProfileId)
            .ToDictionary(x => x.Key, x => CanManageWithinScope(x));
        var items = profiles.Select(x => ToDto(
            x,
            assignmentMap.GetValueOrDefault(x.Id, Array.Empty<StaffAssignment>()),
            manageabilityMap.GetValueOrDefault(x.Id))).ToArray();
        return StaffResult<StaffPage>.Success(new(items, page, pageSize, total));
    }

    public async Task<StaffResult<StaffProfileDto>> UpdateAsync(
        Guid staffId, StaffProfileUpdateInput input, CancellationToken cancellationToken)
    {
        var profile = await ManageableProfiles().FirstOrDefaultAsync(x => x.Id == staffId, cancellationToken);
        if (profile is null) return StaffResult<StaffProfileDto>.Failure("Staff profile not found.", 404);

        var givenName = Normalize(input.GivenName);
        var middleName = Normalize(input.MiddleName);
        var familyName = Normalize(input.FamilyName);
        var designation = Normalize(input.ProviderDesignation);
        if (givenName is null || givenName.Length > 100 ||
            middleName?.Length > 100 || familyName?.Length > 100 || designation?.Length > 100)
            return StaffResult<StaffProfileDto>.Failure("Given name is required; name and designation fields must be at most 100 characters.", 400);

        profile.GivenName = givenName;
        profile.MiddleName = middleName;
        profile.FamilyName = familyName;
        profile.ProviderDesignation = designation;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(staffId, cancellationToken);
    }

    public async Task<StaffResult<StaffProfileDto>> SetActiveAsync(
        Guid staffId, bool isActive, CancellationToken cancellationToken)
    {
        var profile = await ManageableProfiles().FirstOrDefaultAsync(x => x.Id == staffId, cancellationToken);
        if (profile is null) return StaffResult<StaffProfileDto>.Failure("Staff profile not found.", 404);
        profile.IsActive = isActive;
        if (!isActive)
        {
            var activeAssignments = await db.StaffAssignments
                .Where(x => x.StaffProfileId == staffId && x.IsActive && x.EffectiveTo == null)
                .ToListAsync(cancellationToken);
            foreach (var assignment in activeAssignments)
            {
                assignment.IsActive = false;
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                assignment.EffectiveTo = today < assignment.EffectiveFrom ? assignment.EffectiveFrom : today;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(staffId, cancellationToken);
    }

    public async Task<StaffResult<StaffAssignmentDto>> AddAssignmentAsync(
        Guid staffId, StaffAssignmentInput input, CancellationToken cancellationToken)
    {
        var profile = await ManageableProfiles().FirstOrDefaultAsync(x => x.Id == staffId, cancellationToken);
        if (profile is null) return StaffResult<StaffAssignmentDto>.Failure("Staff profile not found.", 404);
        if (input.EffectiveFrom == default)
            return StaffResult<StaffAssignmentDto>.Failure("Assignment effective start date is required.", 400);
        if (!profile.IsActive)
            return StaffResult<StaffAssignmentDto>.Failure("An inactive staff profile cannot receive a new assignment.", 409);
        if (!await CanManageAssignmentAsync(input.HospitalId, input.DepartmentId, cancellationToken))
            return StaffResult<StaffAssignmentDto>.Failure("Hospital or department not found.", 404);

        var duplicate = await db.StaffAssignments.AnyAsync(x =>
            x.StaffProfileId == staffId && x.HospitalId == input.HospitalId &&
            x.DepartmentId == input.DepartmentId && x.IsActive && x.EffectiveTo == null,
            cancellationToken);
        if (duplicate) return StaffResult<StaffAssignmentDto>.Failure("An active assignment already exists for this location.", 409);

        var assignment = new StaffAssignment
        {
            StaffProfileId = staffId,
            HospitalId = input.HospitalId,
            DepartmentId = input.DepartmentId,
            EffectiveFrom = input.EffectiveFrom
        };
        db.StaffAssignments.Add(assignment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: "23505" })
        {
            return StaffResult<StaffAssignmentDto>.Failure("An active assignment already exists for this location.", 409);
        }
        return StaffResult<StaffAssignmentDto>.Success(ToDto(assignment));
    }

    public async Task<StaffResult<StaffAssignmentDto>> EndAssignmentAsync(
        Guid staffId, Guid assignmentId, StaffAssignmentEndInput input, CancellationToken cancellationToken)
    {
        var profile = await ManageableProfiles().FirstOrDefaultAsync(x => x.Id == staffId, cancellationToken);
        if (profile is null) return StaffResult<StaffAssignmentDto>.Failure("Staff profile not found.", 404);
        var assignment = await db.StaffAssignments.FirstOrDefaultAsync(
            x => x.Id == assignmentId && x.StaffProfileId == staffId, cancellationToken);
        if (assignment is null ||
            !await CanManageAssignmentAsync(assignment.HospitalId, assignment.DepartmentId, cancellationToken))
            return StaffResult<StaffAssignmentDto>.Failure("Assignment not found.", 404);
        if (input.EffectiveTo < assignment.EffectiveFrom)
            return StaffResult<StaffAssignmentDto>.Failure("Assignment end date cannot precede its start date.", 400);
        if (assignment.EffectiveTo.HasValue)
            return StaffResult<StaffAssignmentDto>.Failure("Assignment has already ended.", 409);

        assignment.EffectiveTo = input.EffectiveTo;
        assignment.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return StaffResult<StaffAssignmentDto>.Success(ToDto(assignment));
    }

    private IQueryable<StaffProfile> ReadableProfiles()
    {
        IQueryable<StaffProfile> query = db.StaffProfiles;
        if (organizationScope.IsTenantWide) return query;
        var hospitalIds = organizationScope.HospitalIds.ToArray();
        var departmentIds = organizationScope.DepartmentIds.ToArray();
        return query.Where(profile => db.StaffAssignments.Any(assignment =>
            assignment.StaffProfileId == profile.Id &&
            (hospitalIds.Contains(assignment.HospitalId) ||
             (assignment.DepartmentId.HasValue && departmentIds.Contains(assignment.DepartmentId.Value)))));
    }

    private bool IsReadableAssignment(StaffAssignment assignment) =>
        organizationScope.IsTenantWide ||
        organizationScope.HospitalIds.Contains(assignment.HospitalId) ||
        (assignment.DepartmentId.HasValue &&
         organizationScope.DepartmentIds.Contains(assignment.DepartmentId.Value));

    private bool CanManageWithinScope(IEnumerable<StaffAssignment> assignments)
    {
        var values = assignments.ToArray();
        if (values.Length == 0) return false;
        if (organizationScope.IsTenantWide) return true;

        var hospitalIds = organizationScope.HospitalIds.ToHashSet();
        var departmentIds = organizationScope.DepartmentIds.ToHashSet();
        return values.All(assignment =>
            hospitalIds.Contains(assignment.HospitalId) ||
            (assignment.DepartmentId.HasValue && departmentIds.Contains(assignment.DepartmentId.Value)));
    }

    private IQueryable<StaffProfile> ManageableProfiles()
    {
        if (organizationScope.IsTenantWide) return db.StaffProfiles;
        var hospitalIds = organizationScope.HospitalIds.ToArray();
        var departmentIds = organizationScope.DepartmentIds.ToArray();
        return db.StaffProfiles.Where(profile =>
            db.StaffAssignments.Any(assignment => assignment.StaffProfileId == profile.Id) &&
            !db.StaffAssignments.Any(assignment =>
                assignment.StaffProfileId == profile.Id &&
                !hospitalIds.Contains(assignment.HospitalId) &&
                (!assignment.DepartmentId.HasValue ||
                 !departmentIds.Contains(assignment.DepartmentId.Value))));
    }

    private async Task<bool> CanManageAssignmentAsync(Guid hospitalId, Guid? departmentId, CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty) return false;
        var hospitalExists = await db.Hospitals.AnyAsync(x => x.Id == hospitalId, cancellationToken);
        if (!hospitalExists) return false;
        if (departmentId is null) return organizationScope.CanManageHospital(hospitalId);
        return await db.Departments.AnyAsync(x => x.Id == departmentId && x.HospitalId == hospitalId, cancellationToken) &&
               organizationScope.CanManageDepartment(hospitalId, departmentId.Value);
    }

    private static StaffProfileDto ToDto(
        StaffProfile profile,
        IReadOnlyList<StaffAssignment>? profileAssignments,
        bool canManageWithinScope)
    {
        var assignments = profileAssignments?.Select(ToDto).ToArray() ?? [];
        return new StaffProfileDto(profile.Id, profile.StaffCode, profile.GivenName, profile.MiddleName,
            profile.FamilyName, profile.ProviderDesignation, profile.IsActive,
            profile.CreatedAtUtc, profile.UpdatedAtUtc, assignments, canManageWithinScope);
    }

    private static StaffAssignmentDto ToDto(StaffAssignment assignment) =>
        new(assignment.Id, assignment.HospitalId, assignment.DepartmentId,
            assignment.EffectiveFrom, assignment.EffectiveTo, assignment.IsActive);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeCode(string? value)
    {
        var normalized = Normalize(value)?.ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
