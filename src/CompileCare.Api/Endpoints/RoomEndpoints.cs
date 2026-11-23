using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Api;

public static class RoomEndpoints
{
    public static IEndpointRouteBuilder MapRoomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/wards/{wardId:guid}/rooms").WithTags("Rooms");
        group.MapGet("/", async (Guid wardId, CompileCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Rooms.AsNoTracking().Where(x => x.WardId == wardId).OrderBy(x => x.Name)
                .Select(x => new RoomResponse(x.Id, x.WardId, x.Name)).ToListAsync(ct)));
        group.MapGet("/{id:guid}", async (Guid wardId, Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(x => x.WardId == wardId && x.Id == id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(RoomResponse.From(entity));
        });
        group.MapPost("/", async (Guid wardId, RoomRequest request, CompileCareDbContext db, CancellationToken ct) =>
        {
            if (!await db.Wards.AnyAsync(x => x.Id == wardId, ct)) return Results.NotFound();
            var name = request.Name?.Trim();
            var invalid = Validate(name); if (invalid is not null) return invalid;
            if (await db.Rooms.AnyAsync(x => x.WardId == wardId && x.Name == name, ct)) return Duplicate();
            var entity = new Room { WardId = wardId, Name = name! }; db.Rooms.Add(entity);
            try { await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/wards/{wardId}/rooms/{entity.Id}", RoomResponse.From(entity)); }
            catch (DbUpdateException ex) when (Unique(ex)) { return Duplicate(); }
        });
        group.MapPut("/{id:guid}", async (Guid wardId, Guid id, RoomRequest request, CompileCareDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Rooms.FirstOrDefaultAsync(x => x.WardId == wardId && x.Id == id, ct);
            if (entity is null) return Results.NotFound();
            var name = request.Name?.Trim();
            var invalid = Validate(name); if (invalid is not null) return invalid;
            if (await db.Rooms.AnyAsync(x => x.WardId == wardId && x.Id != id && x.Name == name, ct)) return Duplicate();
            entity.Name = name!;
            try { await db.SaveChangesAsync(ct); return Results.Ok(RoomResponse.From(entity)); }
            catch (DbUpdateConcurrencyException) { return ConflictConcurrency(); }
            catch (DbUpdateException ex) when (Unique(ex)) { return Duplicate(); }
        });
        group.MapDelete("/{id:guid}", async (Guid wardId, Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Rooms.FirstOrDefaultAsync(x => x.WardId == wardId && x.Id == id, ct);
            if (entity is null) return Results.NotFound();
            db.Remove(entity);
            try { await db.SaveChangesAsync(ct); return Results.NoContent(); }
            catch (DbUpdateConcurrencyException) { return ConflictConcurrency(); }
            catch (DbUpdateException ex) when (ForeignKey(ex) { return Results.Conflict(new { message = "Room cannot be deleted while it contains beds." }); }
        });
        return endpoints;
    }
    private static IResult? Validate(string? name) => string.IsNullOrWhiteSpace(name) || name.Length > 100
        ? Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required and must be at most 100 characters."] }) : null;
    private static bool Unique(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private static bool ForeignKey(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
    private static IResult Duplicate() => Results.Conflict(new { message = "A room with this name already exists in the ward." });
    private static IResult ConflictConcurrency() => Results.Conflict(new { message = "The room changed concurrently. Reload and retry." });
}
public sealed record RoomRequest(string? Name);
public sealed record RoomResponse(Guid Id, Guid WardId, string Name)
{
    public static RoomResponse From(Room x) => new(x.Id, x.WardId, x.Name);
}
