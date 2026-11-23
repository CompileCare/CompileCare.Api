using CompileCare.Application.Facilities;
namespace CompileCare.Api;
public static class RoomEndpoints
{
    public static IEndpointRouteBuilder MapRoomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/wards/{wardId:guid}/rooms").WithTags("Rooms");
        group.MapGet("/", async (Guid wardId, IFacilityService service, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            Results.Ok(await service.ListRooms(wardId, new(page, pageSize), ct)));
        group.MapGet("/{id:guid}", async (Guid wardId, Guid id, IFacilityService service, CancellationToken ct) =>
            FacilityHttp.From(await service.GetRoom(wardId, id, ct)));
        group.MapPost("/", async (Guid wardId, RoomRequest request, IFacilityService service, CancellationToken ct) =>
            FacilityHttp.Created(await service.CreateRoom(wardId, new(request.Name), ct), $"/api/v1/wards/{wardId}/rooms"));
        group.MapPut("/{id:guid}", async (Guid wardId, Guid id, RoomRequest request, IFacilityService service, CancellationToken ct) =>
            FacilityHttp.From(await service.UpdateRoom(wardId, id, new(request.Name), ct)));
        group.MapDelete("/{id:guid}", async (Guid wardId, Guid id, IFacilityService service, CancellationToken ct) =>
            FacilityHttp.Deleted(await service.DeleteRoom(wardId, id, ct)));
        return endpoints;
    }
}
public sealed record RoomRequest(string? Name);