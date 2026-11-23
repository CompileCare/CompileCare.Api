using CompileCare.Application.Facilities;
namespace CompileCare.Api;
public static class DepartmentEndpoints
{
 public static IEndpointRouteBuilder MapDepartmentEndpoints(this IEndpointRouteBuilder e)
 {
  var g=e.MapGroup("/api/v1/hospitals/{hospitalId:guid}/departments").WithTags("Departments");
  g.MapGet("/",async(Guid hospitalId,IFacilityService s,CancellationToken ct)=>Results.Ok(await s.ListDepartments(hospitalId,ct)));
  g.MapGet("/{id:guid}",async(Guid hospitalId,Guid id,IFacilityService s,CancellationToken ct)=>FacilityHttp.From(await s.GetDepartment(hospitalId,id,ct)));
  g.MapPost("/",async(Guid hospitalId,DepartmentRequest r,IFacilityService s,CancellationToken ct)=>FacilityHttp.Created(await s.CreateDepartment(hospitalId,new(r.Name,r.Code),ct),$"/api/v1/hospitals/{hospitalId}/departments"));
  g.MapPut("/{id:guid}",async(Guid hospitalId,Guid id,DepartmentRequest r,IFacilityService s,CancellationToken ct)=>FacilityHttp.From(await s.UpdateDepartment(hospitalId,id,new(r.Name,r.Code),ct)));
  g.MapDelete("/{id:guid}",async(Guid hospitalId,Guid id,IFacilityService s,CancellationToken ct)=>FacilityHttp.Deleted(await s.DeleteDepartment(hospitalId,id,ct)));
  return e;
 }
}
public sealed record DepartmentRequest(string? Name,string? Code);