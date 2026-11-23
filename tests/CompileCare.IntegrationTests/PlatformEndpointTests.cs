using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
namespace CompileCare.IntegrationTests;
public sealed class PlatformEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact] public async Task LiveEndpoint_IsHealthy() { using var client = factory.CreateClient(); var response = await client.GetAsync("/health/live"); Assert.Equal(HttpStatusCode.OK, response.StatusCode); }
    [Fact] public async Task InvalidTenantHeader_IsRejected() { using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Tenant-ID", "bad"); var response = await client.GetAsync("/api/v1/platform/context"); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
}
