using HeapCare.Api.Security;
using HeapCare.Application.Tenancy;
using Microsoft.Extensions.Options;

namespace HeapCare.Api;

public static class TenantMembershipEndpoints
{
    public static IEndpointRouteBuilder MapTenantMembershipEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var platformGroup = endpoints
            .MapGroup("/api/v1/platform/tenants/{tenantId:guid}/initial-administrator")
            .WithTags("Platform tenant memberships")
            .WithMetadata(PlatformOperatorEndpointMetadata.Instance)
            .RequireAuthorization(AuthorizationPolicies.PlatformOperator);

        platformGroup.MapPost("/", async (
                Guid tenantId,
                InitialAdministratorRequest request,
                ITenantMembershipAdministrationService service,
                IOptions<OidcAuthenticationOptions> authenticationOptions,
                CancellationToken cancellationToken) =>
            {
                var result = await service.CreateInitialAdministratorAsync(
                    tenantId,
                    request.Issuer,
                    request.Subject,
                    authenticationOptions.Value.Authority ?? string.Empty,
                    cancellationToken);

                if (!result.IsSuccess || result.Value is null)
                    return TenantMembershipHttp.From(result);

                return result.StatusCode == 201
                    ? Results.Created(
                        $"/api/v1/tenant/memberships/{result.Value.Id:D}",
                        result.Value)
                    : Results.Ok(result.Value);
            })
            .WithName("CreateInitialTenantAdministrator");

        var tenantGroup = endpoints
            .MapGroup("/api/v1/tenant/memberships")
            .WithTags("Tenant memberships")
            .WithMetadata(TenantScopedEndpointMetadata.Instance)
            .RequireAuthorization()
            .RequireAuthorization(AuthorizationPolicies.TenantMembershipManage);

        tenantGroup.MapGet("/", async (
                ITenantMembershipAdministrationService service,
                int page = 1,
                int pageSize = 50,
                CancellationToken cancellationToken = default) =>
                Results.Ok(await service.ListAsync(
                    new TenantMembershipPageRequest(page, pageSize),
                    cancellationToken)))
            .WithName("ListTenantMemberships");

        tenantGroup.MapPost("/", async (
                TenantMembershipRequest request,
                ITenantMembershipAdministrationService service,
                IOptions<OidcAuthenticationOptions> authenticationOptions,
                CancellationToken cancellationToken) =>
            {
                var result = await service.CreateAsync(
                    new TenantMembershipInput(
                        request.Issuer,
                        request.Subject,
                        request.Role,
                        request.Scopes?.Select(scope =>
                            new TenantMembershipScopeInput(
                                scope.ScopeType,
                                scope.HospitalId,
                                scope.DepartmentId)).ToArray()),
                    authenticationOptions.Value.Authority ?? string.Empty,
                    cancellationToken);

                if (!result.IsSuccess || result.Value is null)
                    return TenantMembershipHttp.From(result);

                return Results.Created(
                    $"/api/v1/tenant/memberships/{result.Value.Id:D}",
                    result.Value);
            })
            .WithName("CreateTenantMembership");

        tenantGroup.MapPut("/{membershipId:guid}", async (
                Guid membershipId,
                TenantMembershipUpdateRequest request,
                ITenantMembershipAdministrationService service,
                CancellationToken cancellationToken) =>
                TenantMembershipHttp.From(await service.UpdateAsync(
                    membershipId,
                    new TenantMembershipUpdateInput(
                        request.Role,
                        request.Scopes?.Select(scope =>
                            new TenantMembershipScopeInput(
                                scope.ScopeType,
                                scope.HospitalId,
                                scope.DepartmentId)).ToArray()),
                    cancellationToken)))
            .WithName("UpdateTenantMembership");

        tenantGroup.MapPost("/{membershipId:guid}/deactivate", async (
                Guid membershipId,
                ITenantMembershipAdministrationService service,
                CancellationToken cancellationToken) =>
                TenantMembershipHttp.From(await service.SetActiveAsync(
                    membershipId, false, cancellationToken)))
            .WithName("DeactivateTenantMembership");

        tenantGroup.MapPost("/{membershipId:guid}/reactivate", async (
                Guid membershipId,
                ITenantMembershipAdministrationService service,
                CancellationToken cancellationToken) =>
                TenantMembershipHttp.From(await service.SetActiveAsync(
                    membershipId, true, cancellationToken)))
            .WithName("ReactivateTenantMembership");

        return endpoints;
    }
}

public sealed record InitialAdministratorRequest(string? Issuer, string? Subject);
public sealed record TenantMembershipScopeRequest(
    string? ScopeType,
    Guid? HospitalId = null,
    Guid? DepartmentId = null);
public sealed record TenantMembershipRequest(
    string? Issuer,
    string? Subject,
    string? Role,
    IReadOnlyList<TenantMembershipScopeRequest>? Scopes);
public sealed record TenantMembershipUpdateRequest(
    string? Role,
    IReadOnlyList<TenantMembershipScopeRequest>? Scopes);

internal static class TenantMembershipHttp
{
    public static IResult From<T>(TenantMembershipResult<T> result)
    {
        if (result.IsSuccess)
            return Results.Ok(result.Value);

        var title = result.StatusCode switch
        {
            StatusCodes.Status400BadRequest => "Validation failed",
            StatusCodes.Status403Forbidden => "Access denied",
            StatusCodes.Status404NotFound => "Membership not found",
            StatusCodes.Status409Conflict => "Membership conflict",
            _ => "The membership request could not be completed"
        };

        return Results.Problem(
            statusCode: result.StatusCode,
            title: title,
            detail: result.Error);
    }
}
