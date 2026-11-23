using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Api;

public static class WardEndpoints
{
    public static IEndpointRouteBuilder MapWardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/departments/{departmentId:guid}/wards").WithTags("Wards");
        group.MapGet("/", async (Guid departmentId, CompileCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Wards.AsNoTracking().Where(x => x.DepartmentId == departmentId).OrderBy(x => x.Name)
                .Select(x => new WardResponse(x.Id, x.DepartmentId, x.Name)).ToListAsync(ct)));
        group.MapGet("/{id:guid}", async (Guid departmentId, Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Wards.AsNoTracking().FirstOrDefaultAsync(x => x.DepartmentId == departmentId && x.Id == id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(WardResponse.From(entity));
        });
        group.MapPost("/", async (Guid departmentId, NamedFacilityRequest request, CompileCareDbContext db, CancellationToken ct) =>
        {
            if (!await db.Departments.AnyAsync(x => x.Id == departmentId, ct)) return Results.NotFound();
            var name = request.Name?.Trim();
            var invalid = ValidateName(name, 200); if (invalid is not null) return invalid;
            if (await db.Wards.AnyAsync(x => x.DepartmentId == departmentId && x.Name == name, ct)) return Duplicate();
            var entity = new Ward { DepartmentId = departmentId, Name = name! }; db.Wards.Add(entity);
            try { await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/departments/{departmentId}/wards/{entity.Id}", WardResponse.From(entity)); }
            catch (DbUpdateException ex) when (Unique(ex)) { return Duplicate(); }
        });
        group.MapPut("/{id:guid}", async (Guid departmentId, Guid id, NamedFacilityRequest request, CompileCareDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Wards.FirstOrDefaultAsync(x => x.DepartmentId == departmentId && x.Id == id, ct);
            if (entity is null) return Results.NotFound();
            var name = request.Name?.Trim();
            var invalid = ValidateName(name, 200); if (invalid is not null) return invalid;
            if (await db.Wards.AnyAsync(x => x.DepartmentId == departmentId && x.Id != id && x.Name == name, ct)) return Duplicate();
            entity.Name = name!;
            try { await db.SaveChangesAsync(ct); return Results.Ok(WardResponse.From(entity)); }
            catch (DbUpdateConcurrencyException) { return ConflictConcurrency(); }
            catch (DbUpdateException ex) when (Unique(ex)) { return Duplicate(); }
        });
        group.MapDelete("/{id:guid}", async (Guid departmentId, Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Wards.FirstOrDefaultAsync(x => x.DepartmentId == departmentId && x.Id == id, ct);
            if (entity is null) return Results.NotFound();
            db.Remove(entity);
            try { await db.SaveChangesAsync(ct); return Results.NoContent(); }
            catch (DbUpdateConcurrencyException) { return ConflictConcurrency(); }
            catch (DbUpdateException ex) when (ForeignKey(ex)) { return Results.Conflict(new { message = "Ward cannot be deleted while it contains rooms." }); }
        });
        return endpoints;
    }
    private static IResult? ValidateName(string? name, int max) => string.IsNullOrWhiteSpace(name) || name.Length > max
        ? Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [$"Name is required and must be at most {max} characters."] }) : null;
    private static bool Unique(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private static bool ForeignKey(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
    private static IResult Duplicate() => Results.Conflict(new { message = "A ward with this name already exists in the department." });
    private static IResult ConflictConcurrency() => Results.Conflict(new { message = "The ward changed concurrently. Reload and retry." });
}
public sealed record NamedFacilityRequest(string? Name);
public sealed record WardResponse(Guid Id, Guid DepartmentId, string Name)
{
    public static WardResponse From(Ward x) => new(x.Id, x.DepartmentId, x.Name);
}
