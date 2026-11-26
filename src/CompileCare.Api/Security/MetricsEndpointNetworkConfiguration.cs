using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CompileCare.Api.Security;

public static class MetricsEndpointNetworkConfiguration
{
    public static IPNetwork[] GetAllowedNetworks(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            return [];

        var configuredNetworks = configuration.GetSection("Observability:Metrics:AllowedNetworks").Get<string[]>() ?? [];
        if (configuredNetworks.Length == 0)
        {
            throw new InvalidOperationException(
                "Observability:Metrics:AllowedNetworks must contain at least one trusted CIDR outside Development and Testing.");
        }

        var networks = new List<IPNetwork>(configuredNetworks.Length);
        foreach (var configuredNetwork in configuredNetworks)
        {
            if (string.IsNullOrWhiteSpace(configuredNetwork) ||
                !string.Equals(configuredNetwork, configuredNetwork.Trim(), StringComparison.Ordinal) ||
                !configuredNetwork.Contains('/') ||
                !IPNetwork.TryParse(configuredNetwork, out var network))
            {
                throw new InvalidOperationException(
                    $"Observability:Metrics:AllowedNetworks contains an invalid CIDR: '{configuredNetwork}'.");
            }

            networks.Add(network);
        }

        return networks.Distinct().ToArray();
    }
}
