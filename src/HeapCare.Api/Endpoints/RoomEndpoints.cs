using HeapCare.Api.Security;
using HeapCare.Application.Facilities;

namespace HeapCare.Api;

public static class RoomEndpoints
{
    public static IEndpointRouteBuilder MapRoomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/wards/{wardId:guid}/rooms").WithTags("Rooms").WithMetadata(TenantScopedEndpointMetadata.Instance).RequireAuthorization();

        group.MapGet("/", async (
            Guid wardId,
            IRoomService service,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default) =>
                Results.Ok(await service.ListRooms(wardId, new(page, pageSize), ct)))
            .WithName("ListRooms").RequireAuthorization(AuthorizationPolicies.FacilityRead);

        group.MapGet("/{id:guid}", async (
            Guid wardId,
            Guid id,
            IRoomService service,
            CancellationToken ct) =>
                FacilityHttp.From(await service.GetRoom(wardId, id, ct)))
            .WithName("GetRoom").RequireAuthorization(AuthorizationPolicies.FacilityRead);

        group.MapPost("/", async (
            Guid wardId,
            RoomRequest request,
            IRoomService service,
            CancellationToken ct) =>
                FacilityHttp.Created(
                    await service.CreateRoom(wardId, new(request.Name), ct),
                    $"/api/v1/wards/{wardId}/rooms",
                    room => room.Id))
            .WithName("CreateRoom").RequireAuthorization(AuthorizationPolicies.FacilityWrite);

        group.MapPut("/{id:guid}", async (
            Guid wardId,
            Guid id,
            RoomRequest request,
            IRoomService service,
            CancellationToken ct) =>
                FacilityHttp.From(await service.UpdateRoom(wardId, id, new(request.Name), ct)))
            .WithName("UpdateRoom").RequireAuthorization(AuthorizationPolicies.FacilityWrite);

        group.MapDelete("/{id:guid}", async (
            Guid wardId,
            Guid id,
            IRoomService service,
            CancellationToken ct) =>
                FacilityHttp.Deleted(await service.DeleteRoom(wardId, id, ct)))
            .WithName("DeleteRoom").RequireAuthorization(AuthorizationPolicies.FacilityDelete);

        return endpoints;
    }
}

public sealed record RoomRequest(string? Name);
