using CompileCare.Application.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Facilities;

public sealed partial class FacilityService(CompileCareDbContext db) : IFacilityService
{
    private static FacilityResult<T> Missing<T>() => FacilityResult<T>.Failure("Resource not found.", 404);
    private static FacilityResult<T> Invalid<T>(string field, string message) => FacilityResult<T>.Failure($"{field}: {message}", 400);
    private static FacilityResult<T> Duplicate<T>(string message) => FacilityResult<T>.Failure(message, 409);
    private static FacilityResult<T> Concurrency<T>() => FacilityResult<T>.Failure("Resource changed concurrently. Reload and retry.", 409);
    private static bool Unique(DbUpdateException e) => e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private static bool ForeignKey(DbUpdateException e) => e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
    private static string? Clean(string? s) => s?.Trim();
    private static string? CleanCode(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();

}
