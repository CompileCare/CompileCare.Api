using HeapCare.Api.Security;
using HeapCare.Application.Tenancy;

namespace HeapCare.Api;

public static class TenantRegistryEndpoints
{
    public static IEndpointRouteBuilder MapPlatformTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/platform/tenants")
            .WithTags("Platform tenants")
            .WithMetadata(PlatformOperatorEndpointMetadata.Instance)
            .RequireAuthorization(AuthorizationPolicies.PlatformOperator);

        group.MapGet("/", async (
                ITenantRegistryService service,
                int page = 1,
                int pageSize = 50,
                CancellationToken cancellationToken = default) =>
                Results.Ok(await service.ListAsync(new TenantPageRequest(page, pageSize), cancellationToken)))
            .WithName("ListPlatformTenants");

        group.MapGet("/{id:guid}", async (
                Guid id,
                ITenantRegistryService service,
                CancellationToken cancellationToken) =>
                TenantRegistryHttp.From(await service.GetAsync(id, cancellationToken)))
            .WithName("GetPlatformTenant");

        group.MapPost("/", async (
                TenantCreateRequest request,
                ITenantRegistryService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.CreateAsync(
                    new TenantCreateInput(request.Name, request.Slug), cancellationToken);
                if (!result.IsSuccess || result.Value is null)
                    return TenantRegistryHttp.From(result);

                return Results.Created($"/api/v1/platform/tenants/{result.Value.Id:D}", result.Value);
            })
            .WithName("CreatePlatformTenant");

        group.MapPost("/{id:guid}/activate", async (
                Guid id, ITenantRegistryService service, CancellationToken cancellationToken) =>
                TenantRegistryHttp.From(await service.TransitionAsync(id, TenantStatuses.Active, cancellationToken)))
            .WithName("ActivatePlatformTenant");

        group.MapPost("/{id:guid}/suspend", async (
                Guid id, ITenantRegistryService service, CancellationToken cancellationToken) =>
                TenantRegistryHttp.From(await service.TransitionAsync(id, TenantStatuses.Suspended, cancellationToken)))
            .WithName("SuspendPlatformTenant");

        group.MapPost("/{id:guid}/close", async (
                Guid id, ITenantRegistryService service, CancellationToken cancellationToken) =>
                TenantRegistryHttp.From(await service.TransitionAsync(id, TenantStatuses.Closed, cancellationToken)))
            .WithName("ClosePlatformTenant");

        return endpoints;
    }
}

public sealed record TenantCreateRequest(string? Name, string? Slug);

internal static class TenantRegistryHttp
{
    public static IResult From<T>(TenantRegistryResult<T> result)
    {
        if (result.IsSuccess)
            return Results.Ok(result.Value);

        var title = result.StatusCode switch
        {
            StatusCodes.Status400BadRequest => "Validation failed",
            StatusCodes.Status404NotFound => "Tenant not found",
            StatusCodes.Status409Conflict => "Tenant conflict",
            _ => "The request could not be completed"
        };
        return Results.Problem(statusCode: result.StatusCode, title: title, detail: result.Error);
    }
}
