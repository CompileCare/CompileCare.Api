using CompileCare.Application.Facilities;
namespace CompileCare.Api;
public static class WardEndpoints
{
    public static IEndpointRouteBuilder MapWardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/departments/{departmentId:guid}/wards").WithTags("Wards");
        group.MapGet("/", async (Guid departmentId, IFacilityService service, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            Results.Ok(await service.ListWards(departmentId, new(page, pageSize), ct)));
        group.MapGet("/{id:guid}", async (Guid departmentId, Guid id, IFacilityService service, CancellationToken ct) =>
            FacilityHttp.From(await service.GetWard(departmentId, id, ct)));
        group.MapPost("/", async (Guid departmentId, NamedFacilityRequest request, IFacilityService service, CancellationToken ct) =>
            FacilityHttp.Created(await service.CreateWard(departmentId, new(request.Name), ct), $"/api/v1/departments/{departmentId}/wards"));
        group.MapPut("/{id:guid}", async (Guid departmentId, Guid id, NamedFacilityRequest request, IFacilityService service, CancellationToken ct) =>
            FacilityHttp.From(await service.UpdateWard(departmentId, id, new(request.Name), ct)));
        group.MapDelete("/{id:guid}", async (Guid departmentId, Guid id, IFacilityService service, CancellationToken ct) =>
            FacilityHttp.Deleted(await service.DeleteWard(departmentId, id, ct)));
        return endpoints;
    }
}
public sealed record NamedFacilityRequest(string? Name);