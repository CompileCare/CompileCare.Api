using CompileCare.Application.Patients;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Patients;

public sealed class PatientService(
    CompileCareDbContext db,
    IOrganizationScopeContext organizationScope) : IPatientService
{
    public async Task<PatientResult<PatientDto>> RegisterAsync(
        Guid hospitalId,
        PatientRegistrationInput input,
        CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || !HasHospitalWidePatientScope(hospitalId))
            return PatientResult<PatientDto>.Failure("Hospital not found.", 404);

        var givenName = Normalize(input.GivenName);
        var middleName = Normalize(input.MiddleName);
        var familyName = Normalize(input.FamilyName);

        if (string.IsNullOrWhiteSpace(givenName) || givenName.Length > 100)
            return PatientResult<PatientDto>.Failure("Given name is required and must be at most 100 characters.", 400);
        if (middleName?.Length > 100 || familyName?.Length > 100)
            return PatientResult<PatientDto>.Failure("Patient name parts must be at most 100 characters.", 400);
        if (input.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            return PatientResult<PatientDto>.Failure("Date of birth cannot be in the future.", 400);

        var hospitalExists = await db.Hospitals.AnyAsync(x => x.Id == hospitalId, cancellationToken);
        if (!hospitalExists)
            return PatientResult<PatientDto>.Failure("Hospital not found.", 404);

        // A patient master is tenant-owned. This first API slice only exposes it
        // through an explicit registration at the selected hospital; no cross-site
        // search or automatic duplicate linking is performed.
        var patient = new Patient
        {
            GivenName = givenName,
            MiddleName = middleName,
            FamilyName = familyName,
            DateOfBirth = input.DateOfBirth
        };
        var registration = new HospitalPatientRegistration
        {
            PatientId = patient.Id,
            HospitalId = hospitalId,
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };
        db.Patients.Add(patient);
        db.HospitalPatientRegistrations.Add(registration);

        await db.SaveChangesAsync(cancellationToken);
        return PatientResult<PatientDto>.Success(ToDto(patient, registration));
    }

    public async Task<PatientResult<PatientDto>> GetAsync(
        Guid hospitalId,
        Guid patientId,
        CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || patientId == Guid.Empty ||
            !HasHospitalWidePatientScope(hospitalId))
            return PatientResult<PatientDto>.Failure("Patient not found.", 404);

        var result = await db.HospitalPatientRegistrations
            .Where(x => x.HospitalId == hospitalId && x.PatientId == patientId)
            .Select(x => new PatientDto(
                x.Patient.Id,
                x.Patient.GivenName,
                x.Patient.MiddleName,
                x.Patient.FamilyName,
                x.Patient.DateOfBirth,
                x.HospitalId,
                x.RegisteredAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return result is null
            ? PatientResult<PatientDto>.Failure("Patient not found.", 404)
            : PatientResult<PatientDto>.Success(result);
    }

    public async Task<PatientResult<PatientPage>> SearchAsync(
        Guid hospitalId,
        PatientSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (hospitalId == Guid.Empty || !HasHospitalWidePatientScope(hospitalId))
            return PatientResult<PatientPage>.Failure("Hospital not found.", 404);

        var query = db.HospitalPatientRegistrations
            .Where(x => x.HospitalId == hospitalId);

        var term = Normalize(request.Query);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{EscapeLike(term)}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.Patient.GivenName, pattern) ||
                (x.Patient.MiddleName != null && EF.Functions.ILike(x.Patient.MiddleName, pattern)) ||
                (x.Patient.FamilyName != null && EF.Functions.ILike(x.Patient.FamilyName, pattern)));
        }

        var total = await query.CountAsync(cancellationToken);
        var page = request.SafePage;
        var pageSize = request.SafePageSize;
        var items = await query
            .OrderBy(x => x.Patient.FamilyName)
            .ThenBy(x => x.Patient.GivenName)
            .ThenBy(x => x.Patient.Id)
            .Skip(request.Skip)
            .Take(pageSize)
            .Select(x => new PatientDto(
                x.Patient.Id,
                x.Patient.GivenName,
                x.Patient.MiddleName,
                x.Patient.FamilyName,
                x.Patient.DateOfBirth,
                x.HospitalId,
                x.RegisteredAtUtc))
            .ToListAsync(cancellationToken);

        return PatientResult<PatientPage>.Success(new(items, page, pageSize, total));
    }

    private bool HasHospitalWidePatientScope(Guid hospitalId) =>
        organizationScope.IsTenantWide || organizationScope.HospitalIds.Contains(hospitalId);

    private static PatientDto ToDto(Patient patient, HospitalPatientRegistration registration) =>
        new(patient.Id, patient.GivenName, patient.MiddleName, patient.FamilyName,
            patient.DateOfBirth, registration.HospitalId, registration.RegisteredAtUtc);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
