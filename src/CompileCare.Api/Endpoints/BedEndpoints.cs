using CompileCare.Application.Facilities;
namespace CompileCare.Api;
public static class BedEndpoints
{
 public static IEndpointRouteBuilder MapBedEndpoints(this IEndpointRouteBuilder e)
 {
  var g=e.MapGroup("/api/v1/rooms/{roomId:guid}/beds").WithTags("Beds");
  g.MapGet("/",async(Guid roomId,IFacilityService s,CancellationToken ct)=>Results.Ok(await s.ListBeds(roomId,ct)));
  g.MapGet("/{id:guid}",async(Guid roomId,Guid id,IFacilityService s,CancellationToken ct)=>FacilityHttp.From(await s.GetBed(roomId,id,ct)));
  g.MapPost("/",async(Guid roomId,BedRequest r,IFacilityService s,CancellationToken ct)=>FacilityHttp.Created(await s.CreateBed(roomId,new(r.Number,r.IsActive),ct),$"/api/v1/rooms/{roomId}/beds"));
  g.MapPut("/{id:guid}",async(Guid roomId,Guid id,BedRequest r,IFacilityService s,CancellationToken ct)=>FacilityHttp.From(await s.UpdateBed(roomId,id,new(r.Number,r.IsActive),ct)));
  g.MapDelete("/{id:guid}",async(Guid roomId,Guid id,IFacilityService s,CancellationToken ct)=>FacilityHttp.Deleted(await s.DeleteBed(roomId,id,ct)));
  return e;
 }
}
public sealed record BedRequest(string? Number,bool IsActive=true);