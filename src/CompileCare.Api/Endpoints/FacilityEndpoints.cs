using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
            var name = request.Name?.Trim();
            var code = request.Code?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required and must be at most 200 characters."] });
            if (code is not null && code.Length > 50)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["Code must be at most 50 characters."] });
            if (await db.Hospitals.AnyAsync(x => x.Name == name || (code != null && x.Code == code), ct))
                return Results.Conflict(new { message = "A hospital with this name or code already exists for the tenant." });
            var hospital = new Hospital { Name = name, Code = code };
            db.Hospitals.Add(hospital);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/hospitals/{hospital.Id}", HospitalResponse.From(hospital));
        }).WithName("CreateHospital");

        group.MapPut("/{id:guid}", async (Guid id, UpdateHospitalRequest request, CompileCareDbContext db, CancellationToken ct) =>
        {
            var hospital = await db.Hospitals.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (hospital is null) return Results.NotFound();
            var name = request.Name?.Trim();
            var code = request.Code?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required and must be at most 200 characters."] });
            if (code is not null && code.Length > 50)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["Code must be at most 50 characters."] });
            if (await db.Hospitals.AnyAsync(x => x.Id != id && (x.Name == name || (code != null && x.Code == code)), ct))
                return Results.Conflict(new { message = "A hospital with this name or code already exists for the tenant." });
            hospital.Name = name;
            hospital.Code = code;
            await db.SaveChangesAsync(ct);
            return Results.Ok(HospitalResponse.From(hospital));
        }).WithName("UpdateHospital");

        group.MapDelete("/{id:guid}", async (Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var hospital = await db.Hospitals.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (hospital is null) return Results.NotFound();
            db.Hospitals.Remove(hospital);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithName("DeleteHospital");

        return endpoints;
    }
}

public sealed record CreateHospitalRequest(string? Name, string? Code);
public sealed record UpdateHospitalRequest(string? Name, string? Code);
public sealed record HospitalResponse(Guid Id, string Name, string? Code)
{
    public static HospitalResponse From(Hospital x) => new(x.Id, x.Name, x.Code);
}
