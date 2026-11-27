using HeapCare.Api.Security;
using HeapCare.Application.Facilities;

namespace HeapCare.Api;

public static class WardEndpoints
{
    public static IEndpointRouteBuilder MapWardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/departments/{departmentId:guid}/wards").WithTags("Wards").WithMetadata(TenantScopedEndpointMetadata.Instance).RequireAuthorization();

        group.MapGet("/", async (
            Guid departmentId,
            IWardService service,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default) =>
                Results.Ok(await service.ListWards(departmentId, new(page, pageSize), ct)))
            .WithName("ListWards").RequireAuthorization(AuthorizationPolicies.FacilityRead);

        group.MapGet("/{id:guid}", async (
            Guid departmentId,
            Guid id,
            IWardService service,
            CancellationToken ct) =>
                FacilityHttp.From(await service.GetWard(departmentId, id, ct)))
            .WithName("GetWard").RequireAuthorization(AuthorizationPolicies.FacilityRead);

        group.MapPost("/", async (
            Guid departmentId,
            NamedFacilityRequest request,
            IWardService service,
            CancellationToken ct) =>
                FacilityHttp.Created(
                    await service.CreateWard(departmentId, new(request.Name), ct),
                    $"/api/v1/departments/{departmentId}/wards",
                    ward => ward.Id))
            .WithName("CreateWard").RequireAuthorization(AuthorizationPolicies.FacilityWrite);

        group.MapPut("/{id:guid}", async (
            Guid departmentId,
            Guid id,
            NamedFacilityRequest request,
            IWardService service,
            CancellationToken ct) =>
                FacilityHttp.From(await service.UpdateWard(departmentId, id, new(request.Name), ct)))
            .WithName("UpdateWard").RequireAuthorization(AuthorizationPolicies.FacilityWrite);

        group.MapDelete("/{id:guid}", async (
            Guid departmentId,
            Guid id,
            IWardService service,
            CancellationToken ct) =>
                FacilityHttp.Deleted(await service.DeleteWard(departmentId, id, ct)))
            .WithName("DeleteWard").RequireAuthorization(AuthorizationPolicies.FacilityDelete);

        return endpoints;
    }
}

public sealed record NamedFacilityRequest(string? Name);
