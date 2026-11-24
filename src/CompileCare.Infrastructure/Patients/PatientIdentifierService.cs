using System.Globalization;
using System.Text.RegularExpressions;
using CompileCare.Application.Patients;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Patients;

public sealed partial class PatientIdentifierService(
    CompileCareDbContext db,
    IOrganizationScopeContext organizationScope) : IPatientIdentifierService
{
    private const int MaximumIdentifierLength = 40;

    public async Task<PatientResult<PatientIdentifierPolicyDto>> CreatePolicyAsync(
        Guid hospitalId,
        PatientIdentifierPolicyInput input,
        CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || !HasHospitalWideScope(hospitalId))
            return PatientResult<PatientIdentifierPolicyDto>.Failure("Hospital not found.", 404);

        var prefix = NormalizePrefix(input.Prefix);
        if (prefix is null || !PrefixPattern().IsMatch(prefix))
            return PatientResult<PatientIdentifierPolicyDto>.Failure(
                "Prefix must contain 1–20 uppercase letters, digits or hyphens and start with a letter or digit.", 400);

        if (input.NumberLength is < 1 or > 12 || prefix.Length + input.NumberLength > MaximumIdentifierLength)
            return PatientResult<PatientIdentifierPolicyDto>.Failure(
                "Number length must be between 1 and 12 and the resulting MRN must not exceed 40 characters.", 400);

        var tenantId = db.CurrentTenantId;
        if (tenantId is null || !await db.Hospitals.AnyAsync(x => x.Id == hospitalId, cancellationToken))
            return PatientResult<PatientIdentifierPolicyDto>.Failure("Hospital not found.", 404);

        await using var ownedTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // Serialize policy creation for one hospital. The request audit middleware
        // normally owns this transaction; the local transaction keeps this service
        // safe when invoked outside an HTTP request as well.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "Id" FROM "hospitals" WHERE "TenantId" = {tenantId.Value} AND "Id" = {hospitalId} FOR UPDATE""",
            cancellationToken);

        var existing = await db.HospitalPatientIdentifierPolicies
            .SingleOrDefaultAsync(x => x.HospitalId == hospitalId, cancellationToken);
        if (existing is not null)
        {
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            return PatientResult<PatientIdentifierPolicyDto>.Failure(
                "An MRN policy already exists for this hospital. Policy replacement is not supported by this initial increment.", 409);
        }

        var policy = new HospitalPatientIdentifierPolicy
        {
            HospitalId = hospitalId,
            Prefix = prefix,
            NumberLength = input.NumberLength,
            NextNumber = 1
        };
        db.HospitalPatientIdentifierPolicies.Add(policy);
        await db.SaveChangesAsync(cancellationToken);

        if (ownedTransaction is not null)
            await ownedTransaction.CommitAsync(cancellationToken);

        return PatientResult<PatientIdentifierPolicyDto>.Success(ToDto(policy));
    }

    public async Task<PatientResult<PatientIdentifierPolicyDto>> GetPolicyAsync(
        Guid hospitalId,
        CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || !HasHospitalWideScope(hospitalId))
            return PatientResult<PatientIdentifierPolicyDto>.Failure("Hospital not found.", 404);

        var policy = await db.HospitalPatientIdentifierPolicies
            .SingleOrDefaultAsync(x => x.HospitalId == hospitalId, cancellationToken);
        return policy is null
            ? PatientResult<PatientIdentifierPolicyDto>.Failure("MRN policy not found.", 404)
            : PatientResult<PatientIdentifierPolicyDto>.Success(ToDto(policy));
    }

    public async Task<PatientResult<PatientIdentifierDto>> AssignMedicalRecordNumberAsync(
        Guid hospitalId,
        Guid patientId,
        CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || patientId == Guid.Empty || !HasHospitalWideScope(hospitalId))
            return PatientResult<PatientIdentifierDto>.Failure("Patient registration not found.", 404);

        var tenantId = db.CurrentTenantId;
        if (tenantId is null)
            return PatientResult<PatientIdentifierDto>.Failure("Patient registration not found.", 404);

        var registrationExists = await db.HospitalPatientRegistrations
            .AnyAsync(x => x.HospitalId == hospitalId && x.PatientId == patientId, cancellationToken);
        if (!registrationExists)
            return PatientResult<PatientIdentifierDto>.Failure("Patient registration not found.", 404);

        await using var ownedTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // The policy row is the per-hospital sequence lock. PostgreSQL holds it
        // until the surrounding request/audit transaction commits or rolls back.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "Id" FROM "hospital_patient_identifier_policies" WHERE "TenantId" = {tenantId.Value} AND "HospitalId" = {hospitalId} FOR UPDATE""",
            cancellationToken);

        var policy = await db.HospitalPatientIdentifierPolicies
            .SingleOrDefaultAsync(x => x.HospitalId == hospitalId, cancellationToken);
        if (policy is null)
        {
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            return PatientResult<PatientIdentifierDto>.Failure("MRN policy has not been configured for this hospital.", 409);
        }

        // Repeated and concurrent requests for the same patient return the
        // existing MRN and do not consume another sequence number.
        var existing = await db.PatientIdentifiers.SingleOrDefaultAsync(
            x => x.HospitalId == hospitalId &&
                 x.PatientId == patientId &&
                 x.TypeCode == PatientIdentifierTypes.MedicalRecordNumber,
            cancellationToken);

        if (existing is not null)
        {
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            return PatientResult<PatientIdentifierDto>.Success(ToDto(existing));
        }

        if (policy.NextNumber == long.MaxValue)
            return PatientResult<PatientIdentifierDto>.Failure("The hospital MRN sequence is exhausted.", 409);

        var suffix = policy.NextNumber.ToString($"D{policy.NumberLength}", CultureInfo.InvariantCulture);
        var value = $"{policy.Prefix}{suffix}";
        if (value.Length > MaximumIdentifierLength)
            return PatientResult<PatientIdentifierDto>.Failure("The configured MRN sequence has exceeded the supported length.", 409);

        var identifier = new PatientIdentifier
        {
            PatientId = patientId,
            HospitalId = hospitalId,
            TypeCode = PatientIdentifierTypes.MedicalRecordNumber,
            Value = value,
            NormalizedValue = NormalizeIdentifier(value),
            AssignedAtUtc = DateTimeOffset.UtcNow
        };

        policy.NextNumber++;
        db.PatientIdentifiers.Add(identifier);
        await db.SaveChangesAsync(cancellationToken);

        if (ownedTransaction is not null)
            await ownedTransaction.CommitAsync(cancellationToken);

        return PatientResult<PatientIdentifierDto>.Success(ToDto(identifier));
    }

    public async Task<PatientResult<IReadOnlyList<PatientIdentifierDto>>> GetPatientIdentifiersAsync(
        Guid hospitalId,
        Guid patientId,
        CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || patientId == Guid.Empty || !HasHospitalWideScope(hospitalId))
            return PatientResult<IReadOnlyList<PatientIdentifierDto>>.Failure("Patient registration not found.", 404);

        var registrationExists = await db.HospitalPatientRegistrations
            .AnyAsync(x => x.HospitalId == hospitalId && x.PatientId == patientId, cancellationToken);
        if (!registrationExists)
            return PatientResult<IReadOnlyList<PatientIdentifierDto>>.Failure("Patient registration not found.", 404);

        var identifiers = await db.PatientIdentifiers
            .Where(x => x.HospitalId == hospitalId && x.PatientId == patientId)
            .OrderBy(x => x.AssignedAtUtc)
            .Select(x => new PatientIdentifierDto(
                x.Id, x.PatientId, x.HospitalId, x.TypeCode, x.Value, x.AssignedAtUtc))
            .ToListAsync(cancellationToken);

        return PatientResult<IReadOnlyList<PatientIdentifierDto>>.Success(identifiers);
    }

    public async Task<PatientResult<PatientIdentifierLookupDto>> FindByMedicalRecordNumberAsync(
        Guid hospitalId,
        string? medicalRecordNumber,
        CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || !HasHospitalWideScope(hospitalId))
            return PatientResult<PatientIdentifierLookupDto>.Failure("Patient not found.", 404);

        var normalized = NormalizeIdentifier(medicalRecordNumber);
        if (normalized.Length is 0 or > MaximumIdentifierLength)
            return PatientResult<PatientIdentifierLookupDto>.Failure("Patient not found.", 404);

        var result = await db.PatientIdentifiers
            .Where(x => x.HospitalId == hospitalId &&
                        x.TypeCode == PatientIdentifierTypes.MedicalRecordNumber &&
                        x.NormalizedValue == normalized)
            .Select(x => new PatientIdentifierLookupDto(
                new PatientDto(
                    x.Patient.Id,
                    x.Patient.GivenName,
                    x.Patient.MiddleName,
                    x.Patient.FamilyName,
                    x.Patient.DateOfBirth,
                    x.HospitalId,
                    db.HospitalPatientRegistrations
                        .Where(r => r.HospitalId == x.HospitalId && r.PatientId == x.PatientId)
                        .Select(r => r.RegisteredAtUtc)
                        .First()),
                new PatientIdentifierDto(
                    x.Id, x.PatientId, x.HospitalId, x.TypeCode, x.Value, x.AssignedAtUtc)))
            .FirstOrDefaultAsync(cancellationToken);

        return result is null
            ? PatientResult<PatientIdentifierLookupDto>.Failure("Patient not found.", 404)
            : PatientResult<PatientIdentifierLookupDto>.Success(result);
    }

    private bool HasHospitalWideScope(Guid hospitalId) =>
        organizationScope.IsTenantWide || organizationScope.HospitalIds.Contains(hospitalId);

    private static PatientIdentifierPolicyDto ToDto(HospitalPatientIdentifierPolicy policy) =>
        new(policy.HospitalId, policy.Prefix, policy.NumberLength);

    private static PatientIdentifierDto ToDto(PatientIdentifier identifier) =>
        new(identifier.Id, identifier.PatientId, identifier.HospitalId,
            identifier.TypeCode, identifier.Value, identifier.AssignedAtUtc);

    private static string? NormalizePrefix(string? prefix) =>
        string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim().ToUpperInvariant();

    private static string NormalizeIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();
}
