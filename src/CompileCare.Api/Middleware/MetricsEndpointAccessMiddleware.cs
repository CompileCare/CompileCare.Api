using System.Net;

namespace CompileCare.Api.Middleware;

public sealed class MetricsEndpointAccessMiddleware(
    RequestDelegate next,
    IReadOnlyCollection<IPNetwork> allowedNetworks)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!string.Equals(context.Request.Path.Value, "/metrics", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress is null || !allowedNetworks.Any(network => network.Contains(remoteAddress)))
        {
            // Hide the scrape endpoint from clients outside the monitoring network.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    }
}
