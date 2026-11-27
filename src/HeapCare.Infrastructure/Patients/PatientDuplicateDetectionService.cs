using HeapCare.Application.Patients;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HeapCare.Infrastructure.Patients;

public sealed class PatientDuplicateDetectionService(
    HeapCareDbContext db,
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

        var normalizedGivenName = PatientNameNormalizer.Normalize(request.GivenName);
        var normalizedMiddleName = PatientNameNormalizer.Normalize(request.MiddleName);
        var normalizedFamilyName = PatientNameNormalizer.Normalize(request.FamilyName);

        if (normalizedGivenName is null || request.GivenName!.Length > 100)
            return PatientResult<PatientDuplicateCheckDto>.Failure(
                "Given name is required and must be at most 100 characters.", 400);

        if (request.MiddleName?.Length > 100 || request.FamilyName?.Length > 100)
            return PatientResult<PatientDuplicateCheckDto>.Failure(
                "Patient name parts must be at most 100 characters.", 400);

        if (request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            return PatientResult<PatientDuplicateCheckDto>.Failure(
                "Date of birth cannot be in the future.", 400);

        var hospitalExists = await db.Hospitals.AnyAsync(x => x.Id == hospitalId, cancellationToken);
        if (!hospitalExists)
            return PatientResult<PatientDuplicateCheckDto>.Failure("Hospital not found.", 404);

        // Exact DOB remains mandatory. Without it, the service deliberately does
        // not execute a name-only search.
        if (request.DateOfBirth is null)
            return PatientResult<PatientDuplicateCheckDto>.Success(new(
                false,
                "Candidate matching was not run because date of birth is unavailable. Follow the hospital's manual identity-check procedure.",
                Array.Empty<PatientDuplicateCandidateDto>(),
                false));

        // A candidate must match either the normalized given name, or both
        // normalized middle and family names. A shared family name alone is
        // deliberately insufficient. Results remain hospital-local and advisory.
        var query = db.HospitalPatientRegistrations
            .AsNoTracking()
            .Where(x =>
                x.HospitalId == hospitalId &&
                x.Patient.DateOfBirth == request.DateOfBirth &&
                (x.Patient.NormalizedGivenName == normalizedGivenName ||
                 (normalizedMiddleName != null &&
                  normalizedFamilyName != null &&
                  x.Patient.NormalizedMiddleName == normalizedMiddleName &&
                  x.Patient.NormalizedFamilyName == normalizedFamilyName)))
            .OrderBy(x => x.Patient.FamilyName)
            .ThenBy(x => x.Patient.GivenName)
            .ThenBy(x => x.Patient.Id)
            .Take(MaximumCandidates + 1)
            .Select(x => new CandidateProjection(
                x.Patient.Id,
                x.Patient.GivenName,
                x.Patient.MiddleName,
                x.Patient.FamilyName,
                x.Patient.NormalizedGivenName,
                x.Patient.NormalizedMiddleName,
                x.Patient.NormalizedFamilyName,
                x.Patient.DateOfBirth!.Value,
                x.RegisteredAtUtc));

        var matches = await query.ToListAsync(cancellationToken);
        var hasMoreCandidates = matches.Count > MaximumCandidates;
        var candidates = matches
            .Take(MaximumCandidates)
            .Select(candidate => ToDto(
                candidate,
                normalizedGivenName,
                normalizedMiddleName,
                normalizedFamilyName))
            .ToArray();

        var message = candidates.Length == 0
            ? "No candidates matched the configured checks. This does not prove that no duplicate exists."
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
        string normalizedGivenName,
        string? normalizedMiddleName,
        string? normalizedFamilyName)
    {
        var givenNameMatches = candidate.NormalizedGivenName == normalizedGivenName;
        var middleNameMatches = normalizedMiddleName is not null &&
                                candidate.NormalizedMiddleName == normalizedMiddleName;
        var familyNameMatches = normalizedFamilyName is not null &&
                                candidate.NormalizedFamilyName == normalizedFamilyName;
        var signals = new List<string> { "Exact date of birth" };

        if (givenNameMatches)
            signals.Add("Normalized given name match");
        if (middleNameMatches)
            signals.Add("Normalized middle name match");
        if (familyNameMatches)
            signals.Add("Normalized family name match");
        if (!givenNameMatches && middleNameMatches && familyNameMatches)
            signals.Add("Middle and family names matched; given name differed");

        return new(
            candidate.PatientId,
            candidate.GivenName,
            candidate.MiddleName,
            candidate.FamilyName,
            candidate.DateOfBirth,
            candidate.RegisteredAtUtc,
            signals);
    }

    private sealed record CandidateProjection(
        Guid PatientId,
        string GivenName,
        string? MiddleName,
        string? FamilyName,
        string NormalizedGivenName,
        string? NormalizedMiddleName,
        string? NormalizedFamilyName,
        DateOnly DateOfBirth,
        DateTimeOffset RegisteredAtUtc);
}
