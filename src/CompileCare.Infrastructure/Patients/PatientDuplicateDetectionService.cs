using CompileCare.Application.Patients;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Patients;

public sealed class PatientDuplicateDetectionService(
    CompileCareDbContext db,
    IOrganizationScopeContext organizationScope) : IPatientDuplicateDetectionService
{
    private const int MaximumCandidates = 20;

    public async Task<PatientResult<PatientDuplicateCheckDto>> FindCandidatesAsync(
        Guid hospitalId,
        PatientDuplicateCheckRequest request,
        CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || !HasHospitalWideScope(hospitalId))
            return PatientResult<PatientDuplicateCheckDto>.Failure("Hospital not found.", 404);

        var givenName = Normalize(request.GivenName);
        var middleName = Normalize(request.MiddleName);
        var familyName = Normalize(request.FamilyName);

        if (string.IsNullOrWhiteSpace(givenName) || givenName.Length > 100)
            return PatientResult<PatientDuplicateCheckDto>.Failure(
                "Given name is required and must be at most 100 characters.", 400);

        if (middleName?.Length > 100 || familyName?.Length > 100)
            return PatientResult<PatientDuplicateCheckDto>.Failure(
                "Patient name parts must be at most 100 characters.", 400);

        if (request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            return PatientResult<PatientDuplicateCheckDto>.Failure(
                "Date of birth cannot be in the future.", 400);

        var hospitalExists = await db.Hospitals.AnyAsync(x => x.Id == hospitalId, cancellationToken);
        if (!hospitalExists)
            return PatientResult<PatientDuplicateCheckDto>.Failure("Hospital not found.", 404);

        // DOB is required for this conservative first matching pass. A missing
        // DOB must never cause a name-only search that could expose unrelated records.
        if (request.DateOfBirth is null)
            return PatientResult<PatientDuplicateCheckDto>.Success(new(
                false,
                "Candidate matching was not run because date of birth is unavailable. Follow the hospital's manual identity-check procedure.",
                Array.Empty<PatientDuplicateCandidateDto>(),
                false));

        // This v1 check is deliberately hospital-local and advisory. Exact,
        // case-insensitive given-name + DOB matches are candidates only; no
        // patient association, identifier assignment or merge is performed.
        var pattern = EscapeLike(givenName);
        var matches = await db.HospitalPatientRegistrations
            .AsNoTracking()
            .Where(x =>
                x.HospitalId == hospitalId &&
                x.Patient.DateOfBirth == request.DateOfBirth &&
                EF.Functions.ILike(x.Patient.GivenName, pattern))
            .OrderBy(x => x.Patient.FamilyName)
            .ThenBy(x => x.Patient.GivenName)
            .ThenBy(x => x.Patient.Id)
            .Take(MaximumCandidates + 1)
            .Select(x => new CandidateProjection(
                x.Patient.Id,
                x.Patient.GivenName,
                x.Patient.MiddleName,
                x.Patient.FamilyName,
                x.Patient.DateOfBirth!.Value,
                x.RegisteredAtUtc))
            .ToListAsync(cancellationToken);

        var hasMoreCandidates = matches.Count > MaximumCandidates;
        var candidates = matches
            .Take(MaximumCandidates)
            .Select(candidate => ToDto(candidate, middleName, familyName))
            .ToArray();

        var message = candidates.Length == 0
            ? "No candidates matched the configured conservative checks."
            : "Possible matches found. Verify identity using the hospital's approved procedure; candidates are not confirmed identities.";

        return PatientResult<PatientDuplicateCheckDto>.Success(new(
            true,
            message,
            candidates,
            hasMoreCandidates));
    }

    private bool HasHospitalWideScope(Guid hospitalId) =>
        organizationScope.IsTenantWide || organizationScope.HospitalIds.Contains(hospitalId);

    private static PatientDuplicateCandidateDto ToDto(
        CandidateProjection candidate,
        string? requestedMiddleName,
        string? requestedFamilyName)
    {
        var signals = new List<string>
        {
            "Exact given name (case-insensitive)",
            "Exact date of birth"
        };

        if (requestedMiddleName is not null &&
            string.Equals(requestedMiddleName, Normalize(candidate.MiddleName), StringComparison.OrdinalIgnoreCase))
            signals.Add("Exact middle name");

        if (requestedFamilyName is not null &&
            string.Equals(requestedFamilyName, Normalize(candidate.FamilyName), StringComparison.OrdinalIgnoreCase))
            signals.Add("Exact family name");

        return new(
            candidate.PatientId,
            candidate.GivenName,
            candidate.MiddleName,
            candidate.FamilyName,
            candidate.DateOfBirth,
            candidate.RegisteredAtUtc,
            signals);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private sealed record CandidateProjection(
        Guid PatientId,
        string GivenName,
        string? MiddleName,
        string? FamilyName,
        DateOnly DateOfBirth,
        DateTimeOffset RegisteredAtUtc);
}
