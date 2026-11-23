using CompileCare.Application.Facilities;
namespace CompileCare.Api;
public static class HospitalEndpoints
{
    public static IEndpointRouteBuilder MapHospitalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/hospitals").WithTags("Hospitals");
        group.MapGet("/",async(IFacilityService service,CancellationToken ct)=>Results.Ok(await service.ListHospitals(ct))).WithName("ListHospitals");
        group.MapGet("/{id:guid}",async(Guid id,IFacilityService service,CancellationToken ct)=>FacilityHttp.From(await service.GetHospital(id,ct))).WithName("GetHospital");
        group.MapPost("/",async(HospitalRequest request,IFacilityService service,CancellationToken ct)=>{
            var result=await service.CreateHospital(new(request.Name,request.Code),ct);
            return result.IsSuccess?Results.Created($"/api/v1/hospitals/{result.Value!.Id}",result.Value):FacilityHttp.From(result);
        }).WithName("CreateHospital");
        group.MapPut("/{id:guid}",async(Guid id,HospitalRequest request,IFacilityService service,CancellationToken ct)=>FacilityHttp.From(await service.UpdateHospital(id,new(request.Name,request.Code),ct))).WithName("UpdateHospital");
        group.MapDelete("/{id:guid}",async(Guid id,IFacilityService service,CancellationToken ct)=>FacilityHttp.Deleted(await service.DeleteHospital(id,ct))).WithName("DeleteHospital");
        return endpoints;
    }
}
public sealed record HospitalRequest(string? Name,string? Code);