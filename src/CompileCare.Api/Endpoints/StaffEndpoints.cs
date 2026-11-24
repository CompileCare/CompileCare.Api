using CompileCare.Api.Security;
using CompileCare.Application.Staff;

namespace CompileCare.Api;

public static class StaffEndpoints
{
    public static IEndpointRouteBuilder MapStaffEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/staff")
            .WithTags("Staff")
            .WithMetadata(TenantScopedEndpointMetadata.Instance)
            .RequireAuthorization();

        group.MapPost("/", async (StaffProfileCreateInput request, IStaffManagementService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/staff/{result.Value!.Id:D}", result.Value)
                : Results.Problem(title: "Staff profile creation failed", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("CreateStaffProfile").RequireAuthorization(AuthorizationPolicies.StaffManage);

        group.MapGet("/", async (IStaffManagementService service, CancellationToken ct, string? query = null, int page = 1, int pageSize = 50) =>
        {
            var result = await service.ListAsync(new(query, page, pageSize), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Staff search failed", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("ListStaffProfiles").RequireAuthorization(AuthorizationPolicies.StaffRead);

        group.MapGet("/{staffId:guid}", async (Guid staffId, IStaffManagementService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(staffId, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Staff profile not found", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("GetStaffProfile").RequireAuthorization(AuthorizationPolicies.StaffRead);

        group.MapPut("/{staffId:guid}", async (Guid staffId, StaffProfileUpdateInput request, IStaffManagementService service, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(staffId, request, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Staff profile update failed", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("UpdateStaffProfile").RequireAuthorization(AuthorizationPolicies.StaffManage);

        group.MapPost("/{staffId:guid}/deactivate", async (Guid staffId, IStaffManagementService service, CancellationToken ct) =>
        {
            var result = await service.SetActiveAsync(staffId, false, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Staff profile deactivation failed", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("DeactivateStaffProfile").RequireAuthorization(AuthorizationPolicies.StaffManage);

        group.MapPost("/{staffId:guid}/reactivate", async (Guid staffId, IStaffManagementService service, CancellationToken ct) =>
        {
            var result = await service.SetActiveAsync(staffId, true, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Staff profile reactivation failed", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("ReactivateStaffProfile").RequireAuthorization(AuthorizationPolicies.StaffManage);

        group.MapPost("/{staffId:guid}/assignments", async (Guid staffId, StaffAssignmentInput request, IStaffManagementService service, CancellationToken ct) =>
        {
            var result = await service.AddAssignmentAsync(staffId, request, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/staff/{staffId:D}/assignments/{result.Value!.Id:D}", result.Value)
                : Results.Problem(title: "Staff assignment creation failed", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("CreateStaffAssignment").RequireAuthorization(AuthorizationPolicies.StaffManage);

        group.MapPost("/{staffId:guid}/assignments/{assignmentId:guid}/end", async (Guid staffId, Guid assignmentId, StaffAssignmentEndInput request, IStaffManagementService service, CancellationToken ct) =>
        {
            var result = await service.EndAssignmentAsync(staffId, assignmentId, request, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Staff assignment end failed", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("EndStaffAssignment").RequireAuthorization(AuthorizationPolicies.StaffManage);

        return endpoints;
    }
}
