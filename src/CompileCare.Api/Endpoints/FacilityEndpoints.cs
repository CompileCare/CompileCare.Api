using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Facilities;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Api;

public static class FacilityEndpoints
{
    public static IEndpointRouteBuilder MapFacilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/facilities").WithTags("Facilities");

        group.MapGet("/", async (CompileCareDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Facilities.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new FacilityResponse(x.Id, x.Name, x.Code, x.City, x.State, x.IsActive))
                .ToListAsync(ct)))
            .WithName("ListFacilities");

        group.MapGet("/{id:guid}", async (Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var facility = await db.Facilities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return facility is null ? Results.NotFound() : Results.Ok(FacilityResponse.From(facility));
        }).WithName("GetFacility");

        group.MapPost("/", async (CreateFacilityRequest request, CompileCareDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            if (tenant.TenantId is null || tenant.TenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Tenant context required");
            var name = request.Name?.Trim();
            var code = request.Code?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required and must be at most 200 characters."] });
            if (string.IsNullOrWhiteSpace(code) || code.Length > 50)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["Code is required and must be at most 50 characters."] });
            if (await db.Facilities.AnyAsync(x => x.Code == code, ct))
                return Results.Conflict(new { message = "A facility with this code already exists for the tenant." });
            var facility = new Facility { Name = name, Code = code, AddressLine1 = request.AddressLine1?.Trim(), AddressLine2 = request.AddressLine2?.Trim(), City = request.City?.Trim(), State = request.State?.Trim(), PostalCode = request.PostalCode?.Trim(), PhoneNumber = request.PhoneNumber?.Trim() };
            db.Facilities.Add(facility);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/facilities/{facility.Id}", FacilityResponse.From(facility));
        }).WithName("CreateFacility");

        group.MapPut("/{id:guid}", async (Guid id, UpdateFacilityRequest request, CompileCareDbContext db, CancellationToken ct) =>
        {
            var facility = await db.Facilities.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (facility is null) return Results.NotFound();
            var name = request.Name?.Trim();
            var code = request.Code?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required and must be at most 200 characters."] });
            if (string.IsNullOrWhiteSpace(code) || code.Length > 50)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["Code is required and must be at most 50 characters."] });
            if (await db.Facilities.AnyAsync(x => x.Id != id && x.Code == code, ct))
                return Results.Conflict(new { message = "A facility with this code already exists for the tenant." });
            facility.Name = name; facility.Code = code; facility.AddressLine1 = request.AddressLine1?.Trim(); facility.AddressLine2 = request.AddressLine2?.Trim(); facility.City = request.City?.Trim(); facility.State = request.State?.Trim(); facility.PostalCode = request.PostalCode?.Trim(); facility.PhoneNumber = request.PhoneNumber?.Trim(); facility.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(FacilityResponse.From(facility));
        }).WithName("UpdateFacility");

        group.MapDelete("/{id:guid}", async (Guid id, CompileCareDbContext db, CancellationToken ct) =>
        {
            var facility = await db.Facilities.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (facility is null) return Results.NotFound();
            db.Facilities.Remove(facility);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithName("DeleteFacility");

        return endpoints;
    }
}

public sealed record CreateFacilityRequest(string? Name, string? Code, string? AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode, string? PhoneNumber);
public sealed record UpdateFacilityRequest(string? Name, string? Code, string? AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode, string? PhoneNumber, bool IsActive);
public sealed record FacilityResponse(Guid Id, string Name, string Code, string? City, string? State, bool IsActive)
{
    public static FacilityResponse From(Facility x) => new(x.Id, x.Name, x.Code, x.City, x.State, x.IsActive);
}