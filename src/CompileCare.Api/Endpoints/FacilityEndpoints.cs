using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Api;

public static class HospitalEndpoints
{
    public static IEndpointRouteBuilder MapHospitalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hospitals").WithTags("Hospitals");

        group.MapGet("/", async (CompileCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Hospitals.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new HospitalResponse(x.Id, x.Name, x.Code))
                .ToListAsync(ct)))
            .WithName("ListHospitals");

        group.MapGet("/{id:guid}", async (Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var hospital = await db.Hospitals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return hospital is null ? Results.NotFound() : Results.Ok(HospitalResponse.From(hospital));
        }).WithName("GetHospital");

        group.MapPost("/", async (CreateHospitalRequest request, CompileCareDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            if (tenant.TenantId is null || tenant.TenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Tenant context required");

            var name = NormalizeName(request.Name);
            var code = NormalizeCode(request.Code);
            var validation = Validate(name, code);
            if (validation is not null) return validation;

            if (await db.Hospitals.AnyAsync(x => x.Name == name || (code != null && x.Code == code), ct))
                return DuplicateConflict();

            var hospital = new Hospital { Name = name!, Code = code };
            db.Hospitals.Add(hospital);
            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/v1/hospitals/{hospital.Id}", HospitalResponse.From(hospital));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return DuplicateConflict();
            }
        }).WithName("CreateHospital");

        group.MapPut("/{id:guid}", async (Guid id, UpdateHospitalRequest request, CompileCareDbContext db, CancellationToken ct) =>
        {
            var hospital = await db.Hospitals.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (hospital is null) return Results.NotFound();

            var name = NormalizeName(request.Name);
            var code = NormalizeCode(request.Code);
            var validation = Validate(name, code);
            if (validation is not null) return validation;

            if (await db.Hospitals.AnyAsync(x => x.Id != id && (x.Name == name || (code != null && x.Code == code)), ct))
                return DuplicateConflict();

            hospital.Name = name!;
            hospital.Code = code;
            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(HospitalResponse.From(hospital));
            }
            catch (DbUpdateConcurrencyException)
            {
                return ConcurrencyConflict();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return DuplicateConflict();
            }
        }).WithName("UpdateHospital");

        group.MapDelete("/{id:guid}", async (Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var hospital = await db.Hospitals.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (hospital is null) return Results.NotFound();

            db.Hospitals.Remove(hospital);
            try
            {
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            }
            catch (DbUpdateConcurrencyException)
            {
                return ConcurrencyConflict();
            }
            catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
            {
                return Results.Conflict(new { message = "Hospital cannot be deleted while it contains departments." });
            }
        }).WithName("DeleteHospital");

        return endpoints;
    }

    private static string? NormalizeName(string? name) => name?.Trim();
    private static string? NormalizeCode(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    private static IResult? Validate(string? name, string? code)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required and must be at most 200 characters."] });
        if (code is not null && code.Length > 50)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["Code must be at most 50 characters."] });
        return null;
    }

    private static IResult DuplicateConflict() =>
        Results.Conflict(new { message = "A hospital with this name or code already exists for the tenant." });

    private static IResult ConcurrencyConflict() =>
        Results.Conflict(new { message = "The hospital was changed by another request. Reload and retry." });

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
}

public sealed record CreateHospitalRequest(string? Name, string? Code);
public sealed record UpdateHospitalRequest(string? Name, string? Code);
public sealed record HospitalResponse(Guid Id, string Name, string? Code)
{
    public static HospitalResponse From(Hospital x) => new(x.Id, x.Name, x.Code);
}
