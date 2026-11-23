using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Api;

public static class BedEndpoints
{
    public static IEndpointRouteBuilder MapBedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/rooms/{roomId:guid}/beds").WithTags("Beds");
        group.MapGet("/", async (Guid roomId, CompileCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Beds.AsNoTracking().Where(x => x.RoomId == roomId).OrderBy(x => x.Number)
                .Select(x => new BedResponse(x.Id, x.RoomId, x.Number, x.IsActive)).ToListAsync(ct)));
        group.MapGet("/{id:guid}", async (Guid roomId, Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Beds.AsNoTracking().FirstOrDefaultAsync(x => x.RoomId == roomId && x.Id == id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(BedResponse.From(entity));
        });
        group.MapPost("/", async (Guid roomId, BedRequest request, CompileCareDbContext db, CancellationToken ct) =>
        {
            if (!await db.Rooms.AnyAsync(x => x.Id == roomId, ct)) return Results.NotFound();
            var number = request.Number?.Trim();
            var invalid = Validate(number); if (invalid is not null) return invalid;
            if (await db.Beds.AnyAsync(x => x.RoomId == roomId && x.Number == number, ct)) return Duplicate();
            var entity = new Bed { RoomId = roomId, Number = number!, IsActive = request.IsActive }; db.Beds.Add(entity);
            try { await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/rooms/{roomId}/beds/{entity.Id}", BedResponse.From(entity)); }
            catch (DbUpdateException ex) when (Unique(ex)) { return Duplicate(); }
        });
        group.MapPut("/{id:guid}", async (Guid roomId, Guid id, BedRequest request, CompileCareDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Beds.FirstOrDefaultAsync(x => x.RoomId == roomId && x.Id == id, ct);
            if (entity is null) return Results.NotFound();
            var number = request.Number?.Trim();
            var invalid = Validate(number); if (invalid is not null) return invalid;
            if (await db.Beds.AnyAsync(x => x.RoomId == roomId && x.Id != id && x.Number == number, ct)) return Duplicate();
            entity.Number = number!; entity.IsActive = request.IsActive;
            try { await db.SaveChangesAsync(ct); return Results.Ok(BedResponse.From(entity)); }
            catch (DbUpdateConcurrencyException) { return ConflictConcurrency(); }
            catch (DbUpdateException ex) when (Unique(ex)) { return Duplicate(); }
        });
        group.MapDelete("/{id:guid}", async (Guid roomId, Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Beds.FirstOrDefaultAsync(x => x.RoomId == roomId && x.Id == id, ct);
            if (entity is null) return Results.NotFound();
            db.Remove(entity);
            try { await db.SaveChangesAsync(ct); return Results.NoContent(); }
            catch (DbUpdateConcurrencyException) { return ConflictConcurrency(); }
        });
        return endpoints;
    }
    private static IResult? Validate(string? number) => string.IsNullOrWhiteSpace(number) || number.Length > 50
        ? Results.ValidationProblem(new Dictionary<string, string[]> { ["number"] = ["Number is required and must be at most 50 characters."] }) : null;
    private static bool Unique(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private static IResult Duplicate() => Results.Conflict(new { message = "A bed with this number already exists in the room." });
    private static IResult ConflictConcurrency() => Results.Conflict(new { message = "The bed changed concurrently. Reload and retry." });
}
public sealed record BedRequest(string? Number, bool IsActive = true);
public sealed record BedResponse(Guid Id, Guid RoomId, string Number, bool IsActive)
{
    public static BedResponse From(Bed x) => new(x.Id, x.RoomId, x.Number, x.IsActive);
}
