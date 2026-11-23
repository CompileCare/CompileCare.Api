using CompileCare.Api.Security;
using CompileCare.Application.Tenancy;
using Serilog.Context;

namespace CompileCare.Api.Middleware;

/// <summary>
/// Resolves the active tenant only after authentication and validates it against
/// tenant membership claims issued by the trusted identity provider.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Tenant-ID";

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var isTenantScoped = context.GetEndpoint()?.Metadata
            .GetMetadata<TenantScopedEndpointMetadata>() is not null;

        if (!isTenantScoped || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var tenantHeaders = context.Request.Headers[HeaderName];
        if (tenantHeaders.Count != 1 ||
            !Guid.TryParse(tenantHeaders[0], out var tenantId) ||
            tenantId == Guid.Empty)
        {
            await Results.Problem(
                title: "Invalid tenant context",
                detail: $"A single, non-empty GUID must be supplied in the {HeaderName} header.",
                statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
            return;
        }

        var hasMembership = context.User
            .FindAll(AuthorizationPolicies.TenantIdClaim)
            .Any(claim => Guid.TryParse(claim.Value, out var permittedTenantId) &&
                          permittedTenantId == tenantId);

        if (!hasMembership)
        {
            await Results.Problem(
                title: "Tenant access denied",
                detail: "The authenticated user is not a member of the requested tenant.",
                statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
            return;
        }

        tenantContext.SetTenant(tenantId);
        using (LogContext.PushProperty("TenantId", tenantId))
        {
            await next(context);
        }
    }
}
