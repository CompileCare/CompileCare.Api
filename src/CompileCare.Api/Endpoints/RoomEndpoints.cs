using CompileCare.Application.Facilities;
namespace CompileCare.Api;
public static class RoomEndpoints
{
 public static IEndpointRouteBuilder MapRoomEndpoints(this IEndpointRouteBuilder e)
 {
  var g=e.MapGroup("/api/v1/wards/{wardId:guid}/rooms").WithTags("Rooms");
  g.MapGet("/",async(Guid wardId,IFacilityService s,CancellationToken ct)=>Results.Ok(await s.ListRooms(wardId,ct)));
  g.MapGet("/{id:guid}",async(Guid wardId,Guid id,IFacilityService s,CancellationToken ct)=>FacilityHttp.From(await s.GetRoom(wardId,id,ct)));
  g.MapPost("/",async(Guid wardId,RoomRequest r,IFacilityService s,CancellationToken ct)=>FacilityHttp.Created(await s.CreateRoom(wardId,new(r.Name),ct),$"/api/v1/wards/{wardId}/rooms"));
  g.MapPut("/{id:guid}",async(Guid wardId,Guid id,RoomRequest r,IFacilityService s,CancellationToken ct)=>FacilityHttp.From(await s.UpdateRoom(wardId,id,new(r.Name),ct)));
  g.MapDelete("/{id:guid}",async(Guid wardId,Guid id,IFacilityService s,CancellationToken ct)=>FacilityHttp.Deleted(await s.DeleteRoom(wardId,id,ct)));
  return e;
 }
}
public sealed record RoomRequest(string? Name);