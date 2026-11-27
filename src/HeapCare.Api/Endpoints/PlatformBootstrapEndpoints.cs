using HeapCare.Api.Security;
using HeapCare.Application.Tenancy;

namespace HeapCare.Api;

public static class PlatformBootstrapEndpoints
{
    public static IEndpointRouteBuilder MapPlatformBootstrapEndpoints(
        this IEndpointRouteBuilder endpoints,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
            return endpoints;

        endpoints.MapPost("/api/v1/platform/bootstrap/operator", async (
                PlatformOperatorBootstrapRequest request,
                HttpContext context,
                IConfiguration configuration,
                IPlatformOperatorBootstrapService service,
                CancellationToken cancellationToken) =>
            {
                var issuer = context.User.FindFirst(AuthorizationPolicies.ValidatedIssuerClaim)?.Value;
                var subject = context.User.FindFirst("sub")?.Value;
                if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                        title: "Validated identity required");

                var result = await service.BootstrapAsync(
                    issuer, subject,
                    configuration["PlatformBootstrap:Secret"] ?? string.Empty,
                    request.BootstrapSecret ?? string.Empty,
                    cancellationToken);

                if (result.InvalidSecret)
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                        title: "Bootstrap secret rejected");
                if (result.AlreadyInitialized)
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                        title: "Platform bootstrap already completed");
                if (!result.Created)
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: "Platform operator could not be provisioned");

                return Results.Created("/api/v1/platform/bootstrap/operator",
                    new { issuer, subject, status = "Platform operator provisioned" });
            })
            .WithTags("Platform bootstrap")
            .WithName("BootstrapFirstPlatformOperator")
            .WithMetadata(PlatformOperatorEndpointMetadata.Instance)
            .RequireAuthorization();

        return endpoints;
    }
}

public sealed record PlatformOperatorBootstrapRequest(string? BootstrapSecret);
