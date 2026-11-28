using HeapCare.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class InpatientRoutePolicyContractTests(FacilityApiFactory factory)
{
    [Fact]
    public void EveryImplementedIpdRoute_IsTenantScopedAuthenticatedAndUsesItsDedicatedPolicy()
    {
        // EndpointDataSource includes metadata inherited from the IPD route group.
        using var client = factory.CreateClient();

        var expectedPolicies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CreateInpatientAdmission"] = AuthorizationPolicies.InpatientAdmit,
            ["ListInpatientAdmissions"] = AuthorizationPolicies.InpatientRead,
            ["GetInpatientAdmission"] = AuthorizationPolicies.InpatientRead,
            ["AssignInpatientBed"] = AuthorizationPolicies.InpatientBedAssign,
            ["TransferInpatientBed"] = AuthorizationPolicies.InpatientBedTransfer
        };

        var ipdEndpoints = factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                (endpoint.RoutePattern.RawText ?? string.Empty)
                    .StartsWith(
                        "/api/v1/hospitals/{hospitalId:guid}/ipd/",
                        StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(expectedPolicies.Count, ipdEndpoints.Length);

        var namedEndpoints = ipdEndpoints
            .Select(endpoint => new
            {
                Endpoint = endpoint,
                Name = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            })
            .ToArray();

        Assert.All(namedEndpoints, item =>
            Assert.False(string.IsNullOrWhiteSpace(item.Name), "Every IPD route must have a stable endpoint name."));

        var duplicateNames = namedEndpoints
            .GroupBy(item => item.Name!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.Empty(duplicateNames);

        Assert.Equal(
            expectedPolicies.Keys.OrderBy(name => name, StringComparer.Ordinal),
            namedEndpoints.Select(item => item.Name!).OrderBy(name => name, StringComparer.Ordinal));

        foreach (var item in namedEndpoints)
        {
            var endpoint = item.Endpoint;
            var endpointName = item.Name!;

            Assert.NotNull(endpoint.Metadata.GetMetadata<TenantScopedEndpointMetadata>());
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());

            var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            Assert.NotEmpty(authorization);
            Assert.Contains(
                authorization,
                metadata => string.Equals(
                    metadata.Policy,
                    expectedPolicies[endpointName],
                    StringComparison.Ordinal));
        }
    }
}
