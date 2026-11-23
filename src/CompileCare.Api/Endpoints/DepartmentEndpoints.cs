using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Api;

public static class DepartmentEndpoints
{
    public static IEndpointRouteBuilder MapDepartmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hospitals/{hospitalId:guid}/departments").WithTags("Departments");

        group.MapGet("/", async (Guid hospitalId, CompileCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Departments.AsNoTracking().Where(x => x.HospitalId == hospitalId)
                .OrderBy(x => x.Name).Select(x => new DepartmentResponse(x.Id, x.HospitalId, x.Name, x.Code)).ToListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid hospitalId, Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.HospitalId == hospitalId && x.Id == id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(DepartmentResponse.From(entity));
        });

        group.MapPost("/", async (Guid hospitalId, DepartmentRequest request, CompileCareDbContext db, CancellationToken ct) =>
        {
            if (!await db.Hospitals.AnyAsync(x => x.Id == hospitalId, ct)) return Results.NotFound();
            var name = request.Name?.Trim();
            var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim().ToUpperInvariant();
            var validation = Validate(name, code);
            if (validation is not null) return validation;
            if (await db.Departments.AnyAsync(x => x.HospitalId == hospitalId && (x.Name == name || (code != null && x.Code == code)), ct))
                return Duplicate();
            var entity = new Department { HospitalId = hospitalId, Name = name!, Code = code };
            db.Departments.Add(entity);
            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/v1/hospitals/{hospitalId}/departments/{entity.Id}", DepartmentResponse.From(entity));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Duplicate(); }
        });

        group.MapPut("/{id:guid}", async (Guid hospitalId, Guid id, DepartmentRequest request, CompileCareDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Departments.FirstOrDefaultAsync(x => x.HospitalId == hospitalId && x.Id == id, ct);
            if (entity is null) return Results.NotFound();
            var name = request.Name?.Trim();
            var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim().ToUpperInvariant();
            var validation = Validate(name, code);
            if (validation is not null) return validation;
            if (await db.Departments.AnyAsync(x => x.HospitalId == hospitalId && x.Id != id && (x.Name == name || (code != null && x.Code == code)), ct))
                return Duplicate();
            entity.Name = name!; entity.Code = code;
            try { await db.SaveChangesAsync(ct); return Results.Ok(DepartmentResponse.From(entity)); }
            catch (DbUpdateConcurrencyException) { return Concurrency(); }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Duplicate(); }
        });

        group.MapDelete("/{id:guid}", async (Guid hospitalId, Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Departments.FirstOrDefaultAsync(x => x.HospitalId == hospitalId && x.Id == id, ct);
            if (entity is null) return Results.NotFound();
            db.Departments.Remove(entity);
            try { await db.SaveChangesAsync(ct); return Results.NoContent(); }
            catch (DbUpdateConcurrencyException) { return Concurrency(); }
            catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
            { return Results.Conflict(new { message = "Department cannot be deleted while it contains wards." }); }
        });

        return endpoints;
    }

    private static IResult? Validate(string? name, string? code)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required and must be at most 200 characters."] });
        if (code is not null && code.Length > 50)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["Code must be at most 50 characters."] });
        return null;
    }
    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private static bool IsForeignKeyViolation(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
    private static IResult Duplicate() => Results.Conflict(new { message = "A department with this name or code already exists in this hospital." });
    private static IResult Concurrency() => Results.Conflict(new { message = "The department was changed by another request. Reload and retry." });
}

public sealed record DepartmentRequest(string? Name, string? Code);
public sealed record DepartmentResponse(Guid Id, Guid HospitalId, string Name, string? Code)
{
    public static DepartmentResponse From(Department x) => new(x.Id, x.HospitalId, x.Name, x.Code);
}
