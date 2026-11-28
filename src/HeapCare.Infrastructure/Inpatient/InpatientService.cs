using HeapCare.Application.Inpatient;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HeapCare.Infrastructure.Inpatient;

public sealed class InpatientService(
    HeapCareDbContext db,
    IOrganizationScopeContext organizationScope) : IInpatientService
{
    public async Task<InpatientOperationResult<InpatientAdmissionPage>> ListAsync(
        Guid hospitalId,
        InpatientAdmissionPageRequest request,
        CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || request is null)
            return InpatientOperationResult<InpatientAdmissionPage>.Failure(
                "A valid hospital and admission-list request are required.", 400);

        var status = string.IsNullOrWhiteSpace(request.Status) ? null : request.Status.Trim();
        if (status is not null)
        {
            status = status.ToUpperInvariant() switch
            {
                "ACTIVE" => InpatientAdmissionStatuses.Active,
                "CANCELLED" => InpatientAdmissionStatuses.Cancelled,
                "DISCHARGED" => InpatientAdmissionStatuses.Discharged,
                _ => null
            };

            if (status is null)
                return InpatientOperationResult<InpatientAdmissionPage>.Failure(
                    "Admission status must be Active, Cancelled or Discharged.", 400);
        }

        var hasHospitalWideScope = organizationScope.IsTenantWide ||
                                   organizationScope.HospitalIds.Contains(hospitalId);
        var visibleDepartmentIds = organizationScope.Scopes
            .Where(scope => scope.ScopeType == OrganizationScopeTypes.Department &&
                            scope.HospitalId == hospitalId &&
                            scope.DepartmentId.HasValue)
            .Select(scope => scope.DepartmentId!.Value)
            .Distinct()
            .ToArray();

        // Department-only membership must not turn the hospital context selector
        // into hospital-wide patient discovery. Only admissions whose current
        // placement is in one of the member's departments are visible.
        if (!hasHospitalWideScope && visibleDepartmentIds.Length == 0)
            return InpatientOperationResult<InpatientAdmissionPage>.Failure(
                "Hospital not found.", 404);

        var query = db.InpatientAdmissions.AsNoTracking()
            .Where(admission => admission.HospitalId == hospitalId);

        if (status is not null)
            query = query.Where(admission => admission.Status == status);

        if (!hasHospitalWideScope)
        {
            query = query.Where(admission =>
                db.InpatientBedPlacements.Any(placement =>
                    placement.HospitalId == hospitalId &&
                    placement.AdmissionId == admission.Id &&
                    placement.EndedAtUtc == null &&
                    visibleDepartmentIds.Contains(placement.DepartmentId)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var page = request.SafePage;
        var pageSize = request.SafePageSize;
        var admissions = await query
            .OrderByDescending(admission => admission.AdmittedAtUtc)
            .ThenBy(admission => admission.Id)
            .Skip(request.Skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var admissionIds = admissions.Select(admission => admission.Id).ToArray();
        var placementQuery = db.InpatientBedPlacements.AsNoTracking()
            .Where(placement => placement.HospitalId == hospitalId &&
                                admissionIds.Contains(placement.AdmissionId));

        if (!hasHospitalWideScope)
            placementQuery = placementQuery.Where(
                placement => visibleDepartmentIds.Contains(placement.DepartmentId));

        var placements = await placementQuery
            .OrderBy(placement => placement.StartedAtUtc)
            .ThenBy(placement => placement.Id)
            .ToListAsync(cancellationToken);

        var placementsByAdmission = placements
            .GroupBy(placement => placement.AdmissionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<InpatientBedPlacement>)group.ToArray());

        var items = admissions.Select(admission =>
        {
            IReadOnlyList<InpatientBedPlacement> history =
                placementsByAdmission.TryGetValue(admission.Id, out var visibleHistory)
                    ? visibleHistory
                    : Array.Empty<InpatientBedPlacement>();
            var current = history.SingleOrDefault(placement => placement.EndedAtUtc is null);
            return ToDto(admission, history, current);
        }).ToArray();

        return InpatientOperationResult<InpatientAdmissionPage>.Success(
            new InpatientAdmissionPage(items, page, pageSize, totalCount));
    }

    public async Task<InpatientOperationResult<InpatientAdmissionDto>> CreateAdmissionAsync(
        Guid hospitalId,
        InpatientAdmissionInput input,
        InpatientActor actor,
        CancellationToken cancellationToken)
    {
        if (!IsValidActor(actor))
            return InpatientOperationResult<InpatientAdmissionDto>.Failure(
                "A validated inpatient actor is required.", 401);
        if (hospitalId == Guid.Empty || input is null || input.PatientId == Guid.Empty)
            return InpatientOperationResult<InpatientAdmissionDto>.Failure(
                "A valid hospital and patient identifier are required.", 400);

        // Admission is a hospital-level operation. A department-only membership
        // cannot create an inpatient episode or infer access to all hospital patients.
        var hasHospitalWideScope = organizationScope.IsTenantWide ||
                                   organizationScope.HospitalIds.Contains(hospitalId);
        if (!hasHospitalWideScope)
            return InpatientOperationResult<InpatientAdmissionDto>.Failure(
                "Hospital not found.", 404);

        // A patient master is not sufficient: the patient must already have an
        // explicit registration at this exact hospital within the current tenant.
        var hasHospitalRegistration = await db.HospitalPatientRegistrations.AnyAsync(
            registration => registration.HospitalId == hospitalId &&
                            registration.PatientId == input.PatientId,
            cancellationToken);
        if (!hasHospitalRegistration)
            return InpatientOperationResult<InpatientAdmissionDto>.Failure(
                "The patient is not registered at the selected hospital.", 404);

        var hasActiveAdmission = await db.InpatientAdmissions.AnyAsync(
            admission => admission.HospitalId == hospitalId &&
                         admission.PatientId == input.PatientId &&
                         admission.Status == InpatientAdmissionStatuses.Active,
            cancellationToken);
        if (hasActiveAdmission)
            return Conflict("The patient already has an active inpatient admission at this hospital.");

        var admission = new InpatientAdmission
        {
            HospitalId = hospitalId,
            PatientId = input.PatientId,
            Status = InpatientAdmissionStatuses.Active,
            AdmittedAtUtc = DateTimeOffset.UtcNow
        };
        db.InpatientAdmissions.Add(admission);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("The admission changed concurrently. Reload and retry.");
        }
        catch (DbUpdateException exception) when (
            FindPostgresError(exception) is { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The filtered unique index is the final arbiter when simultaneous
            // admission requests pass the pre-check at the same time.
            return Conflict("The patient already has an active inpatient admission at this hospital.");
        }
        catch (DbUpdateException exception) when (
            FindPostgresError(exception) is { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // The registration may have become unavailable between validation
            // and persistence. Do not disclose patient or hospital existence.
            return InpatientOperationResult<InpatientAdmissionDto>.Failure(
                "The patient is not registered at the selected hospital.", 404);
        }

        return await GetAsync(hospitalId, admission.Id, cancellationToken);
    }

    public async Task<InpatientOperationResult<InpatientAdmissionDto>> GetAsync(
        Guid hospitalId,
        Guid admissionId,
        CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || admissionId == Guid.Empty)
            return InpatientOperationResult<InpatientAdmissionDto>.Failure(
                "A valid hospital and admission identifier are required.", 400);

        var admission = await db.InpatientAdmissions.AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == admissionId && item.HospitalId == hospitalId,
                cancellationToken);
        if (admission is null)
            return NotFound();

        var placements = await db.InpatientBedPlacements.AsNoTracking()
            .Where(item => item.HospitalId == hospitalId && item.AdmissionId == admissionId)
            .OrderBy(item => item.StartedAtUtc)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var current = placements.SingleOrDefault(item => item.EndedAtUtc is null);
        var hasHospitalWideScope = organizationScope.IsTenantWide ||
                                   organizationScope.HospitalIds.Contains(hospitalId);
        if (!hasHospitalWideScope)
        {
            // A Department grant permits the caller to select its parent hospital
            // context, but it does not grant hospital-wide patient visibility.
            // A department-scoped member may see an admission only when its current
            // placement is in that department. An unplaced admission is intentionally
            // not visible through a department-only grant.
            if (current is null ||
                !organizationScope.CanAccessDepartment(hospitalId, current.DepartmentId))
                return NotFound();

            // Never disclose movements in departments outside the caller's scope.
            placements = placements
                .Where(item => organizationScope.CanAccessDepartment(hospitalId, item.DepartmentId))
                .ToList();
        }

        var visibleCurrent = placements.SingleOrDefault(item => item.EndedAtUtc is null);
        return InpatientOperationResult<InpatientAdmissionDto>.Success(ToDto(admission, placements, visibleCurrent));
    }

    public async Task<InpatientOperationResult<InpatientAdmissionDto>> AssignBedAsync(
        Guid hospitalId,
        Guid admissionId,
        InpatientBedLocation location,
        InpatientActor actor,
        CancellationToken cancellationToken)
    {
        if (!IsValidActor(actor))
            return InpatientOperationResult<InpatientAdmissionDto>.Failure(
                "A validated inpatient actor is required.", 401);
        if (hospitalId == Guid.Empty || admissionId == Guid.Empty || !IsValidLocation(location))
            return InpatientOperationResult<InpatientAdmissionDto>.Failure(
                "A valid hospital, admission and bed location are required.", 400);

        var admission = await db.InpatientAdmissions.FirstOrDefaultAsync(
            item => item.Id == admissionId && item.HospitalId == hospitalId,
            cancellationToken);
        if (admission is null)
            return NotFound();
        if (admission.Status != InpatientAdmissionStatuses.Active)
            return Conflict("A bed can only be assigned to an active inpatient admission.");

        var destination = await ResolveBedAsync(hospitalId, location, cancellationToken);
        if (destination is null)
            return NotFound();
        if (!organizationScope.CanManageDepartment(hospitalId, destination.DepartmentId))
            return NotFound();
        if (!destination.IsActive)
            return Conflict("The selected bed is inactive.");

        var hasCurrentPlacement = await db.InpatientBedPlacements.AnyAsync(
            item => item.AdmissionId == admissionId && item.HospitalId == hospitalId &&
                    item.EndedAtUtc == null,
            cancellationToken);
        if (hasCurrentPlacement)
            return Conflict("The admission already has an active bed placement. Use the transfer operation.");

        db.InpatientBedPlacements.Add(new InpatientBedPlacement
        {
            HospitalId = hospitalId,
            AdmissionId = admissionId,
            DepartmentId = destination.DepartmentId,
            WardId = destination.WardId,
            RoomId = destination.RoomId,
            BedId = destination.BedId,
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("The admission changed concurrently. Reload and retry.");
        }
        catch (DbUpdateException exception) when (IsPlacementConstraintViolation(exception))
        {
            return Conflict("The admission or selected bed has already received an active placement.");
        }

        return await GetAsync(hospitalId, admissionId, cancellationToken);
    }

    public async Task<InpatientOperationResult<InpatientAdmissionDto>> TransferBedAsync(
        Guid hospitalId,
        Guid admissionId,
        InpatientBedTransferInput input,
        InpatientActor actor,
        CancellationToken cancellationToken)
    {
        if (!IsValidActor(actor))
            return InpatientOperationResult<InpatientAdmissionDto>.Failure(
                "A validated inpatient actor is required.", 401);
        if (hospitalId == Guid.Empty || admissionId == Guid.Empty || input is null ||
            !IsValidLocation(input.Destination) ||
            string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 250)
            return InpatientOperationResult<InpatientAdmissionDto>.Failure(
                "A valid destination bed and transfer reason (1–250 characters) are required.", 400);

        var admission = await db.InpatientAdmissions.FirstOrDefaultAsync(
            item => item.Id == admissionId && item.HospitalId == hospitalId,
            cancellationToken);
        if (admission is null)
            return NotFound();
        if (admission.Status != InpatientAdmissionStatuses.Active)
            return Conflict("Only an active inpatient admission can be transferred.");

        var current = await db.InpatientBedPlacements.FirstOrDefaultAsync(
            item => item.AdmissionId == admissionId && item.HospitalId == hospitalId &&
                    item.EndedAtUtc == null,
            cancellationToken);
        if (current is null)
            return Conflict("The admission has no active bed placement to transfer.");

        if (!organizationScope.CanManageDepartment(hospitalId, current.DepartmentId))
            return NotFound();

        var destination = await ResolveBedAsync(hospitalId, input.Destination, cancellationToken);
        if (destination is null)
            return NotFound();
        if (!organizationScope.CanManageDepartment(hospitalId, destination.DepartmentId))
            return NotFound();
        if (!destination.IsActive)
            return Conflict("The selected destination bed is inactive.");
        if (destination.BedId == current.BedId)
            return Conflict("The destination bed is already the current bed.");

        var transferAtUtc = DateTimeOffset.UtcNow;
        if (transferAtUtc <= current.StartedAtUtc)
            transferAtUtc = current.StartedAtUtc.AddTicks(1);

        current.EndedAtUtc = transferAtUtc;
        current.EndReason = input.Reason.Trim();
        db.InpatientBedPlacements.Add(new InpatientBedPlacement
        {
            HospitalId = hospitalId,
            AdmissionId = admissionId,
            DepartmentId = destination.DepartmentId,
            WardId = destination.WardId,
            RoomId = destination.RoomId,
            BedId = destination.BedId,
            StartedAtUtc = transferAtUtc
        });

        try
        {
            // One SaveChanges call persists the old placement closure and the new
            // placement creation atomically. The filtered unique indexes arbitrate
            // concurrent transfers and bed occupancy in PostgreSQL.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("The current bed placement changed concurrently. Reload and retry.");
        }
        catch (DbUpdateException exception) when (IsPlacementConstraintViolation(exception))
        {
            return Conflict("The destination bed or inpatient placement changed concurrently.");
        }

        return await GetAsync(hospitalId, admissionId, cancellationToken);
    }

    private async Task<ResolvedBed?> ResolveBedAsync(
        Guid hospitalId,
        InpatientBedLocation location,
        CancellationToken cancellationToken)
    {
        return await (
            from bed in db.Beds.AsNoTracking()
            join room in db.Rooms.AsNoTracking()
                on new { bed.TenantId, ParentId = bed.RoomId }
                equals new { room.TenantId, ParentId = room.Id }
            join ward in db.Wards.AsNoTracking()
                on new { room.TenantId, ParentId = room.WardId }
                equals new { ward.TenantId, ParentId = ward.Id }
            join department in db.Departments.AsNoTracking()
                on new { ward.TenantId, ParentId = ward.DepartmentId }
                equals new { department.TenantId, ParentId = department.Id }
            where department.HospitalId == hospitalId &&
                  department.Id == location.DepartmentId &&
                  ward.Id == location.WardId &&
                  room.Id == location.RoomId &&
                  bed.Id == location.BedId
            select new ResolvedBed(
                department.Id,
                ward.Id,
                room.Id,
                bed.Id,
                bed.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static InpatientAdmissionDto ToDto(
        InpatientAdmission admission,
        IReadOnlyList<InpatientBedPlacement> placements,
        InpatientBedPlacement? currentPlacement)
    {
        var history = placements.Select(ToDto).ToArray();
        var current = currentPlacement is null ? null : ToDto(currentPlacement);
        return new InpatientAdmissionDto(
            admission.Id,
            admission.HospitalId,
            admission.PatientId,
            admission.Status,
            admission.AdmittedAtUtc,
            admission.CancelledAtUtc,
            admission.DischargedAtUtc,
            current,
            history);
    }

    private static InpatientBedPlacementDto ToDto(InpatientBedPlacement placement) =>
        new(
            placement.Id,
            placement.AdmissionId,
            placement.HospitalId,
            new InpatientBedLocation(
                placement.DepartmentId,
                placement.WardId,
                placement.RoomId,
                placement.BedId),
            placement.StartedAtUtc,
            placement.EndedAtUtc,
            placement.EndReason);

    private static bool IsValidActor(InpatientActor? actor) =>
        actor is not null &&
        !string.IsNullOrWhiteSpace(actor.Issuer) &&
        actor.Issuer.Trim().Length <= 255 &&
        !string.IsNullOrWhiteSpace(actor.Subject) &&
        actor.Subject.Trim().Length <= 255;

    private static bool IsValidLocation(InpatientBedLocation? location) =>
        location is not null &&
        location.DepartmentId != Guid.Empty &&
        location.WardId != Guid.Empty &&
        location.RoomId != Guid.Empty &&
        location.BedId != Guid.Empty;

    private static bool IsPlacementConstraintViolation(DbUpdateException exception) =>
        FindPostgresError(exception) is { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation };

    private static PostgresException? FindPostgresError(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgresException)
                return postgresException;

        return null;
    }

    private static InpatientOperationResult<InpatientAdmissionDto> NotFound() =>
        InpatientOperationResult<InpatientAdmissionDto>.Failure("Inpatient admission not found.", 404);

    private static InpatientOperationResult<InpatientAdmissionDto> Conflict(string error) =>
        InpatientOperationResult<InpatientAdmissionDto>.Failure(error, 409);

    private sealed record ResolvedBed(
        Guid DepartmentId,
        Guid WardId,
        Guid RoomId,
        Guid BedId,
        bool IsActive);
}
