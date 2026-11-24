using CompileCare.Application.Clinical;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Clinical;

public sealed class OpdEncounterService(
    CompileCareDbContext db,
    IOrganizationScopeContext organizationScope) : IOpdEncounterService
{
    public async Task<OpdEncounterResult<OpdEncounterDto>> CreateFromQueueAsync(
        Guid hospitalId, Guid queueEntryId, OpdEncounterActor actor, CancellationToken cancellationToken)
    {
        if (!IsValidActor(actor))
            return OpdEncounterResult<OpdEncounterDto>.Failure("A validated clinical actor is required.", 401);

        var queueEntry = await db.QueueEntries.FirstOrDefaultAsync(
            x => x.Id == queueEntryId && x.HospitalId == hospitalId, cancellationToken);
        if (queueEntry is null || !organizationScope.CanManageDepartment(hospitalId, queueEntry.DepartmentId))
            return OpdEncounterResult<OpdEncounterDto>.Failure("Queue entry not found.", 404);
        if (queueEntry.Status != QueueEntryStatuses.InProgress)
            return OpdEncounterResult<OpdEncounterDto>.Failure("An encounter can only be started from an in-progress queue entry.", 409);
        if (await db.OpdEncounters.AnyAsync(x => x.QueueEntryId == queueEntryId, cancellationToken))
            return OpdEncounterResult<OpdEncounterDto>.Failure("An encounter already exists for this queue entry.", 409);

        var encounter = new OpdEncounter
        {
            HospitalId = queueEntry.HospitalId,
            DepartmentId = queueEntry.DepartmentId,
            PatientId = queueEntry.PatientId,
            QueueEntryId = queueEntry.Id,
            Status = OpdEncounterStatuses.InProgress,
            CreatedByIssuer = actor.Issuer.Trim(),
            CreatedBySubject = actor.Subject.Trim(),
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        db.OpdEncounters.Add(encounter);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: "23505" })
        {
            return OpdEncounterResult<OpdEncounterDto>.Failure(
                "An encounter already exists for this queue entry.", 409);
        }

        return await GetAsync(hospitalId, encounter.Id, cancellationToken);
    }

    public async Task<OpdEncounterResult<OpdEncounterDto>> GetAsync(
        Guid hospitalId, Guid encounterId, CancellationToken cancellationToken)
    {
        var encounter = await db.OpdEncounters.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == encounterId && x.HospitalId == hospitalId, cancellationToken);
        if (encounter is null || !organizationScope.CanAccessDepartment(hospitalId, encounter.DepartmentId))
            return OpdEncounterResult<OpdEncounterDto>.Failure("Encounter not found.", 404);

        var amendments = await db.OpdEncounterAmendments.AsNoTracking()
            .Where(x => x.EncounterId == encounterId)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Select(x => new OpdEncounterAmendmentDto(
                x.Id, x.Reason, x.Content, x.AuthorIssuer, x.AuthorSubject, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return OpdEncounterResult<OpdEncounterDto>.Success(ToDto(encounter, amendments));
    }

    public async Task<OpdEncounterResult<OpdEncounterDto>> UpdateNoteAsync(
        Guid hospitalId, Guid encounterId, OpdEncounterNoteInput input,
        OpdEncounterActor actor, CancellationToken cancellationToken)
    {
        if (!IsValidActor(actor))
            return OpdEncounterResult<OpdEncounterDto>.Failure("A validated clinical actor is required.", 401);
        if (Exceeds(input.ChiefComplaint, 500) || Exceeds(input.History, 4000) ||
            Exceeds(input.Examination, 4000) || Exceeds(input.Assessment, 2000) || Exceeds(input.Plan, 4000))
            return OpdEncounterResult<OpdEncounterDto>.Failure("One or more note sections exceed their allowed length.", 400);

        var encounter = await FindManageableAsync(hospitalId, encounterId, cancellationToken);
        if (encounter is null)
            return OpdEncounterResult<OpdEncounterDto>.Failure("Encounter not found.", 404);
        if (encounter.Status != OpdEncounterStatuses.InProgress)
            return OpdEncounterResult<OpdEncounterDto>.Failure("Only an in-progress encounter can be edited.", 409);

        encounter.ChiefComplaint = Normalize(input.ChiefComplaint);
        encounter.History = Normalize(input.History);
        encounter.Examination = Normalize(input.Examination);
        encounter.Assessment = Normalize(input.Assessment);
        encounter.Plan = Normalize(input.Plan);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(hospitalId, encounterId, cancellationToken);
    }

    public async Task<OpdEncounterResult<OpdEncounterDto>> SignAsync(
        Guid hospitalId, Guid encounterId, OpdEncounterActor actor, CancellationToken cancellationToken)
    {
        if (!IsValidActor(actor))
            return OpdEncounterResult<OpdEncounterDto>.Failure("A validated clinical actor is required.", 401);

        var encounter = await FindManageableAsync(hospitalId, encounterId, cancellationToken);
        if (encounter is null)
            return OpdEncounterResult<OpdEncounterDto>.Failure("Encounter not found.", 404);
        if (encounter.Status != OpdEncounterStatuses.InProgress)
            return OpdEncounterResult<OpdEncounterDto>.Failure("Only an in-progress encounter can be signed.", 409);
        if (string.IsNullOrWhiteSpace(encounter.ChiefComplaint) ||
            string.IsNullOrWhiteSpace(encounter.Assessment))
            return OpdEncounterResult<OpdEncounterDto>.Failure(
                "Chief complaint and assessment are required before signing.", 400);

        encounter.Status = OpdEncounterStatuses.Signed;
        encounter.SignedAtUtc = DateTimeOffset.UtcNow;
        encounter.SignedByIssuer = actor.Issuer.Trim();
        encounter.SignedBySubject = actor.Subject.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(hospitalId, encounterId, cancellationToken);
    }

    public async Task<OpdEncounterResult<OpdEncounterDto>> AddAmendmentAsync(
        Guid hospitalId, Guid encounterId, OpdEncounterAmendmentInput input,
        OpdEncounterActor actor, CancellationToken cancellationToken)
    {
        if (!IsValidActor(actor))
            return OpdEncounterResult<OpdEncounterDto>.Failure("A validated clinical actor is required.", 401);
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 250 ||
            string.IsNullOrWhiteSpace(input.Content) || input.Content.Trim().Length > 4000)
            return OpdEncounterResult<OpdEncounterDto>.Failure(
                "An amendment reason (1–250 characters) and content (1–4000 characters) are required.", 400);

        var encounter = await FindManageableAsync(hospitalId, encounterId, cancellationToken);
        if (encounter is null)
            return OpdEncounterResult<OpdEncounterDto>.Failure("Encounter not found.", 404);
        if (encounter.Status is not (OpdEncounterStatuses.Signed or OpdEncounterStatuses.Closed))
            return OpdEncounterResult<OpdEncounterDto>.Failure("Only a signed encounter can be amended.", 409);

        db.OpdEncounterAmendments.Add(new OpdEncounterAmendment
        {
            EncounterId = encounter.Id,
            HospitalId = encounter.HospitalId,
            DepartmentId = encounter.DepartmentId,
            Reason = input.Reason.Trim(),
            Content = input.Content.Trim(),
            AuthorIssuer = actor.Issuer.Trim(),
            AuthorSubject = actor.Subject.Trim()
        });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(hospitalId, encounterId, cancellationToken);
    }

    public async Task<OpdEncounterResult<OpdEncounterDto>> CloseAsync(
        Guid hospitalId, Guid encounterId, CancellationToken cancellationToken)
    {
        var encounter = await FindManageableAsync(hospitalId, encounterId, cancellationToken);
        if (encounter is null)
            return OpdEncounterResult<OpdEncounterDto>.Failure("Encounter not found.", 404);
        if (encounter.Status != OpdEncounterStatuses.Signed)
            return OpdEncounterResult<OpdEncounterDto>.Failure("Only a signed encounter can be closed.", 409);

        encounter.Status = OpdEncounterStatuses.Closed;
        encounter.ClosedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(hospitalId, encounterId, cancellationToken);
    }

    public async Task<OpdEncounterResult<OpdEncounterDto>> CancelAsync(
        Guid hospitalId, Guid encounterId, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 250)
            return OpdEncounterResult<OpdEncounterDto>.Failure("A cancellation reason (1–250 characters) is required.", 400);

        var encounter = await FindManageableAsync(hospitalId, encounterId, cancellationToken);
        if (encounter is null)
            return OpdEncounterResult<OpdEncounterDto>.Failure("Encounter not found.", 404);
        if (encounter.Status != OpdEncounterStatuses.InProgress)
            return OpdEncounterResult<OpdEncounterDto>.Failure("Only an unsigned in-progress encounter can be cancelled.", 409);

        encounter.Status = OpdEncounterStatuses.Cancelled;
        encounter.CancellationReason = reason.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(hospitalId, encounterId, cancellationToken);
    }

    private Task<OpdEncounter?> FindManageableAsync(
        Guid hospitalId, Guid encounterId, CancellationToken cancellationToken) =>
        db.OpdEncounters.FirstOrDefaultAsync(
            x => x.Id == encounterId && x.HospitalId == hospitalId &&
                 organizationScope.CanManageDepartment(hospitalId, x.DepartmentId),
            cancellationToken);

    private static bool IsValidActor(OpdEncounterActor actor) =>
        !string.IsNullOrWhiteSpace(actor.Issuer) && actor.Issuer.Length <= 255 &&
        !string.IsNullOrWhiteSpace(actor.Subject) && actor.Subject.Length <= 255;

    private static bool Exceeds(string? value, int maxLength) => value?.Length > maxLength;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OpdEncounterDto ToDto(
        OpdEncounter x, IReadOnlyList<OpdEncounterAmendmentDto> amendments) =>
        new(x.Id, x.HospitalId, x.DepartmentId, x.PatientId, x.QueueEntryId, x.Status,
            x.ChiefComplaint, x.History, x.Examination, x.Assessment, x.Plan,
            x.CreatedByIssuer, x.CreatedBySubject, x.StartedAtUtc,
            x.SignedByIssuer, x.SignedBySubject, x.SignedAtUtc, x.ClosedAtUtc,
            x.CancellationReason, amendments);
}
