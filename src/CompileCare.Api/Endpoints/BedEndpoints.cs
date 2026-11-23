using CompileCare.Application.Facilities;
namespace CompileCare.Api;
public static class BedEndpoints
{
    public static IEndpointRouteBuilder MapBedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/rooms/{roomId:guid}/beds").WithTags("Beds");
        group.MapGet("/", async (Guid roomId, IFacilityService service, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            Results.Ok(await service.ListBeds(roomId, new(page, pageSize), ct)));
        group.MapGet("/{id:guid}", async (Guid roomId, Guid id, IFacilityService service, CancellationToken ct) =>
            FacilityHttp.From(await service.GetBed(roomId, id, ct)));
        group.MapPost("/", async (Guid roomId, BedRequest request, IFacilityService service, CancellationToken ct) =>
            FacilityHttp.Created(await service.CreateBed(roomId, new(request.Number, request.IsActive), ct), $"/api/v1/rooms/{roomId}/beds"));
        group.MapPut("/{id:guid}", async (Guid roomId, Guid id, BedRequest request, IFacilityService service, CancellationToken ct) =>
            FacilityHttp.From(await service.UpdateBed(roomId, id, new(request.Number, request.IsActive), ct)));
        group.MapDelete("/{id:guid}", async (Guid roomId, Guid id, IFacilityService service, CancellationToken ct) =>
            FacilityHttp.Deleted(await service.DeleteBed(roomId, id, ct)));
        return endpoints;
    }
}
public sealed record BedRequest(string? Number, bool IsActive = true);