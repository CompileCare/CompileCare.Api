using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CompileCare.Api.Security;

public static class MetricsEndpointConfiguration
{
    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        // Metrics are useful by default in local development and automated tests.
        // Production must opt in explicitly after the scrape route is made private
        // by the selected ingress/network or an authenticated monitoring path.
        return configuration.GetValue<bool?>("Observability:Metrics:Enabled")
            ?? (environment.IsDevelopment() || environment.IsEnvironment("Testing"));
    }
}
