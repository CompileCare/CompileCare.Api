using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Facilities;

public sealed partial class FacilityService(CompileCareDbContext db) : IFacilityService
{
    private static FacilityResult<T> NotFound<T>() =>
        FacilityResult<T>.Failure("Resource not found.", 404);

    private static FacilityResult<T> ValidationFailure<T>(string field, string message) =>
        FacilityResult<T>.Failure($"{field}: {message}", 400);

    private static FacilityResult<T> Conflict<T>(string message) =>
        FacilityResult<T>.Failure(message, 409);

    private static FacilityResult<T> ConcurrencyConflict<T>() =>
        FacilityResult<T>.Failure("Resource changed concurrently. Reload and retry.", 409);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };

    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation
        };

    private static string? NormalizeText(string? value) => value?.Trim();

    private static string? NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
