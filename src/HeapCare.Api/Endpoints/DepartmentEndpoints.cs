using HeapCare.Api.Security;
using HeapCare.Application.Facilities;

namespace HeapCare.Api;

public static class DepartmentEndpoints
{
    public static IEndpointRouteBuilder MapDepartmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hospitals/{hospitalId:guid}/departments").WithTags("Departments").WithMetadata(TenantScopedEndpointMetadata.Instance).RequireAuthorization();

        group.MapGet("/", async (
            Guid hospitalId,
            IDepartmentService service,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default) =>
                FacilityHttp.From(await service.ListDepartments(hospitalId, new(page, pageSize), ct)))
            .WithName("ListDepartments").RequireAuthorization(AuthorizationPolicies.FacilityRead);

        group.MapGet("/{id:guid}", async (
            Guid hospitalId,
            Guid id,
            IDepartmentService service,
            CancellationToken ct) =>
                FacilityHttp.From(await service.GetDepartment(hospitalId, id, ct)))
            .WithName("GetDepartment").RequireAuthorization(AuthorizationPolicies.FacilityRead);

        group.MapPost("/", async (
            Guid hospitalId,
            DepartmentRequest request,
            IDepartmentService service,
            CancellationToken ct) =>
                FacilityHttp.Created(
                    await service.CreateDepartment(hospitalId, new(request.Name, request.Code), ct),
                    $"/api/v1/hospitals/{hospitalId}/departments",
                    department => department.Id))
            .WithName("CreateDepartment").RequireAuthorization(AuthorizationPolicies.FacilityWrite);

        group.MapPut("/{id:guid}", async (
            Guid hospitalId,
            Guid id,
            DepartmentRequest request,
            IDepartmentService service,
            CancellationToken ct) =>
                FacilityHttp.From(await service.UpdateDepartment(hospitalId, id, new(request.Name, request.Code), ct)))
            .WithName("UpdateDepartment").RequireAuthorization(AuthorizationPolicies.FacilityWrite);

        group.MapDelete("/{id:guid}", async (
            Guid hospitalId,
            Guid id,
            IDepartmentService service,
            CancellationToken ct) =>
                FacilityHttp.Deleted(await service.DeleteDepartment(hospitalId, id, ct)))
            .WithName("DeleteDepartment").RequireAuthorization(AuthorizationPolicies.FacilityDelete);

        return endpoints;
    }
}

public sealed record DepartmentRequest(string? Name, string? Code);
