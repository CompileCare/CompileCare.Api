using CompileCare.Infrastructure.Facilities;
using CompileCare.Infrastructure.Persistence;
using CompileCare.Application.Facilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompileCare.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("CompileCare");
        services.AddDbContext<CompileCareDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
                options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(CompileCareDbContext).Assembly.FullName));
        });
        services.AddScoped<IFacilityService, FacilityService>();
        if (!string.IsNullOrWhiteSpace(connectionString))
            services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
        return services;
    }
}