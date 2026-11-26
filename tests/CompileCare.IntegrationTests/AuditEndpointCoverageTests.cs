using CompileCare.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class AuditEndpointCoverageTests
{
    private readonly FacilityApiFactory _factory;

    public AuditEndpointCoverageTests(FacilityApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void EveryAuthenticatedRoute_IsCoveredByRequestAuditMiddleware()
    {
        // Starting the host materializes the final endpoint metadata, including
        // metadata inherited from route groups.
        using var client = _factory.CreateClient();

        var endpoints = _factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        Assert.NotEmpty(endpoints);

        var unauditedRoutes = endpoints
            .Where(endpoint =>
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0 &&
                endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null &&
                !HasAuditMetadata(endpoint))
            .Select(endpoint =>
                $"{endpoint.RoutePattern.RawText ?? endpoint.DisplayName} ({endpoint.DisplayName})")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unauditedRoutes.Length == 0,
            "Authenticated routes must be included in the metadata-only audit pipeline:" +
            Environment.NewLine + string.Join(Environment.NewLine, unauditedRoutes));
    }

    private static bool HasAuditMetadata(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<TenantScopedEndpointMetadata>() is not null ||
        endpoint.Metadata.GetMetadata<UserScopedEndpointMetadata>() is not null ||
        endpoint.Metadata.GetMetadata<PlatformOperatorEndpointMetadata>() is not null;
}
