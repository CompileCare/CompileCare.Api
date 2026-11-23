using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompileCare.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("CompileCare");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<CompileCareDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(CompileCareDbContext).Assembly.FullName)));

            services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
        }

        return services;
    }
}