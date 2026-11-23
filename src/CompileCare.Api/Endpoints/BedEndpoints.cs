using CompileCare.Application.Facilities;

namespace CompileCare.Api;

public static class BedEndpoints
{
    public static IEndpointRouteBuilder MapBedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/rooms/{roomId:guid}/beds").WithTags("Beds");

        group.MapGet("/", async (
            Guid roomId,
            IBedService service,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default) =>
                Results.Ok(await service.ListBeds(roomId, new(page, pageSize), ct)))
            .WithName("ListBeds");

        group.MapGet("/{id:guid}", async (
            Guid roomId,
            Guid id,
            IBedService service,
            CancellationToken ct) =>
                FacilityHttp.From(await service.GetBed(roomId, id, ct)))
            .WithName("GetBed");

        group.MapPost("/", async (
            Guid roomId,
            BedRequest request,
            IBedService service,
            CancellationToken ct) =>
                FacilityHttp.Created(
                    await service.CreateBed(roomId, new(request.Number, request.IsActive), ct),
                    $"/api/v1/rooms/{roomId}/beds",
                    bed => bed.Id))
            .WithName("CreateBed");

        group.MapPut("/{id:guid}", async (
            Guid roomId,
            Guid id,
            BedRequest request,
            IBedService service,
            CancellationToken ct) =>
                FacilityHttp.From(await service.UpdateBed(roomId, id, new(request.Number, request.IsActive), ct)))
            .WithName("UpdateBed");

        group.MapDelete("/{id:guid}", async (
            Guid roomId,
            Guid id,
            IBedService service,
            CancellationToken ct) =>
                FacilityHttp.Deleted(await service.DeleteBed(roomId, id, ct)))
            .WithName("DeleteBed");

        return endpoints;
    }
}

public sealed record BedRequest(string? Number, bool IsActive = true);
