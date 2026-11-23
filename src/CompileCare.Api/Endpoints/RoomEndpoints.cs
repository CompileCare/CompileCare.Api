using CompileCare.Application.Facilities;

namespace CompileCare.Api;

public static class RoomEndpoints
{
    public static IEndpointRouteBuilder MapRoomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/wards/{wardId:guid}/rooms").WithTags("Rooms");

        group.MapGet("/", async (
            Guid wardId,
            IRoomService service,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default) =>
                Results.Ok(await service.ListRooms(wardId, new(page, pageSize), ct)))
            .WithName("ListRooms");

        group.MapGet("/{id:guid}", async (
            Guid wardId,
            Guid id,
            IRoomService service,
            CancellationToken ct) =>
                FacilityHttp.From(await service.GetRoom(wardId, id, ct)))
            .WithName("GetRoom");

        group.MapPost("/", async (
            Guid wardId,
            RoomRequest request,
            IRoomService service,
            CancellationToken ct) =>
                FacilityHttp.Created(
                    await service.CreateRoom(wardId, new(request.Name), ct),
                    $"/api/v1/wards/{wardId}/rooms",
                    room => room.Id))
            .WithName("CreateRoom");

        group.MapPut("/{id:guid}", async (
            Guid wardId,
            Guid id,
            RoomRequest request,
            IRoomService service,
            CancellationToken ct) =>
                FacilityHttp.From(await service.UpdateRoom(wardId, id, new(request.Name), ct)))
            .WithName("UpdateRoom");

        group.MapDelete("/{id:guid}", async (
            Guid wardId,
            Guid id,
            IRoomService service,
            CancellationToken ct) =>
                FacilityHttp.Deleted(await service.DeleteRoom(wardId, id, ct)))
            .WithName("DeleteRoom");

        return endpoints;
    }
}

public sealed record RoomRequest(string? Name);
