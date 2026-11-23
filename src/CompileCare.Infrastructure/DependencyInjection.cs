using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace CompileCare.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        // Database, identity and messaging adapters are registered after their ADRs are approved.
        return services;
    }
}
