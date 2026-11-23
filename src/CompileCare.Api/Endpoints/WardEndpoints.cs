using CompileCare.Application.Facilities;
namespace CompileCare.Api;
public static class WardEndpoints
{
 public static IEndpointRouteBuilder MapWardEndpoints(this IEndpointRouteBuilder e)
 {
  var g=e.MapGroup("/api/v1/departments/{departmentId:guid}/wards").WithTags("Wards");
  g.MapGet("/",async(Guid departmentId,IFacilityService s,CancellationToken ct)=>Results.Ok(await s.ListWards(departmentId,ct)));
  g.MapGet("/{id:guid}",async(Guid departmentId,Guid id,IFacilityService s,CancellationToken ct)=>FacilityHttp.From(await s.GetWard(departmentId,id,ct)));
  g.MapPost("/",async(Guid departmentId,NamedFacilityRequest r,IFacilityService s,CancellationToken ct)=>FacilityHttp.Created(await s.CreateWard(departmentId,new(r.Name),ct),$"/api/v1/departments/{departmentId}/wards"));
  g.MapPut("/{id:guid}",async(Guid departmentId,Guid id,NamedFacilityRequest r,IFacilityService s,CancellationToken ct)=>FacilityHttp.From(await s.UpdateWard(departmentId,id,new(r.Name),ct)));
  g.MapDelete("/{id:guid}",async(Guid departmentId,Guid id,IFacilityService s,CancellationToken ct)=>FacilityHttp.Deleted(await s.DeleteWard(departmentId,id,ct)));
  return e;
 }
}
public sealed record NamedFacilityRequest(string? Name);