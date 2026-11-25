namespace CompileCare.Application.Patients;

public static class PatientPermissions
{
    public const string Read = "patient.read";
    public const string IdentityRead = "patient.identity.read";
    public const string Register = "patient.register";
    public const string SearchAcrossHospitals = "patient.search.cross-hospital";
}

public sealed record PatientRegistrationInput(
    string? GivenName,
    string? MiddleName,
    string? FamilyName,
    DateOnly? DateOfBirth);

public sealed record PatientDto(
    Guid Id,
    string GivenName,
    string? MiddleName,
    string? FamilyName,
    DateOnly? DateOfBirth,
    Guid HospitalId,
    DateTimeOffset RegisteredAtUtc);

public sealed record PatientSearchRequest(string? Query, int Page = 1, int PageSize = 25)
{
    public int SafePage => Page < 1 ? 1 : Math.Min(Page, int.MaxValue / SafePageSize);
    public int SafePageSize => PageSize < 1 ? 25 : Math.Min(PageSize, 100);
    public int Skip => (SafePage - 1) * SafePageSize;
}

public sealed record PatientPage(IReadOnlyList<PatientDto> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}

public sealed record PatientResult<T>(T? Value, string? Error, int StatusCode)
{
    public bool IsSuccess => Error is null;
    public static PatientResult<T> Success(T value) => new(value, null, 200);
    public static PatientResult<T> Failure(string error, int statusCode) => new(default, error, statusCode);
}

public interface IPatientService
{
    Task<PatientResult<PatientDto>> RegisterAsync(Guid hospitalId, PatientRegistrationInput input, CancellationToken cancellationToken);
    Task<PatientResult<PatientDto>> GetAsync(Guid hospitalId, Guid patientId, CancellationToken cancellationToken);
    Task<PatientResult<PatientPage>> SearchAsync(Guid hospitalId, PatientSearchRequest request, CancellationToken cancellationToken);
    Task<PatientResult<PatientPage>> SearchDepartmentAsync(Guid hospitalId, Guid departmentId, PatientSearchRequest request, CancellationToken cancellationToken);
    Task<PatientResult<PatientDto>> GetDepartmentAsync(Guid hospitalId, Guid departmentId, Guid patientId, CancellationToken cancellationToken);
}
