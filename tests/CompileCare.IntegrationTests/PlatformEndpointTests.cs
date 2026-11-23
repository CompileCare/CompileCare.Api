using System.Net;
using Xunit;

namespace CompileCare.IntegrationTests;

public sealed class PlatformEndpointTests(FacilityApiFactory factory) : IClassFixture<FacilityApiFactory>
{
    [Fact]
    public async Task LiveEndpoint_IsHealthy()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlatformContext_RequiresAuthentication()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/platform/context");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidTenantHeader_IsRejected()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        client.DefaultRequestHeaders.Add("X-Test-Tenant-Ids", Guid.NewGuid().ToString("D"));
        client.DefaultRequestHeaders.Add("X-Tenant-ID", "bad");

        var response = await client.GetAsync("/api/v1/platform/context");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
