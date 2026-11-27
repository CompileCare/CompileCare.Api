using System.Net;

namespace HeapCare.Api.Middleware;

public sealed class MetricsEndpointAccessMiddleware(
    RequestDelegate next,
    IReadOnlyCollection<IPNetwork> allowedNetworks)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/metrics", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress?.IsIPv4MappedToIPv6 == true)
        {
            // Dual-mode sockets may represent an IPv4 peer as ::ffff:a.b.c.d.
            // Normalize it so IPv4 CIDR entries behave consistently across hosts.
            remoteAddress = remoteAddress.MapToIPv4();
        }

        if (remoteAddress is null || !allowedNetworks.Any(network => network.Contains(remoteAddress)))
        {
            // Hide the scrape endpoint from clients outside the monitoring network.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    }
}
