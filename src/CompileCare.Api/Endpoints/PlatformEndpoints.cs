using CompileCare.Api.Security;
using CompileCare.Application.Tenancy;

namespace CompileCare.Api;

public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/platform/context", (ITenantContext tenant, HttpContext context) =>
                Results.Ok(new PlatformContextResponse(context.TraceIdentifier, tenant.TenantId)))
            .WithTags("Platform")
            .WithName("GetRequestContext")
            .WithMetadata(TenantScopedEndpointMetadata.Instance)
            .RequireAuthorization();

        return endpoints;
    }
}

public sealed record PlatformContextResponse(string CorrelationId, Guid? TenantId);
