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

        endpoints.MapGet("/api/v1/platform/operator/status", async (
                HttpContext context,
                IPlatformOperatorReader operatorReader,
                CancellationToken cancellationToken) =>
            {
                var issuer = context.User.FindFirst(AuthorizationPolicies.ValidatedIssuerClaim)?.Value;
                var subject = context.User.FindFirst("sub")?.Value;
                if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Validated identity required");
                }

                var isPlatformOperator = await operatorReader.IsActiveAsync(
                    issuer, subject, cancellationToken);
                return Results.Ok(new PlatformOperatorStatusResponse(isPlatformOperator));
            })
            .WithTags("Platform")
            .WithName("GetCurrentPlatformOperatorStatus")
            .WithMetadata(PlatformOperatorEndpointMetadata.Instance)
            .RequireAuthorization();

        return endpoints;
    }
}

public sealed record PlatformContextResponse(string CorrelationId, Guid? TenantId);
public sealed record PlatformOperatorStatusResponse(bool IsPlatformOperator);
