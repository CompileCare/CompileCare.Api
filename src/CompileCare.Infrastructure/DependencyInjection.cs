using CompileCare.Infrastructure.Persistence;
using CompileCare.Infrastructure.Facilities;
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

        // Register the DbContext even when a connection string is not configured.
        // Minimal API endpoint binding needs it recognized as a service at startup;
        // without registration, DbContext parameters are inferred as request bodies
        // and can make unrelated endpoints fail during route initialization.
        services.AddDbContext<CompileCareDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(CompileCareDbContext).Assembly.FullName));
            }
        });

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
        }

        services.AddScoped<CompileCare.Application.Tenancy.ITenantMembershipReader, TenantMembershipReader>();
        services.AddScoped<FacilityService>();
        services.AddScoped<IHospitalService>(sp => sp.GetRequiredService<FacilityService>());
        services.AddScoped<IDepartmentService>(sp => sp.GetRequiredService<FacilityService>());
        services.AddScoped<IWardService>(sp => sp.GetRequiredService<FacilityService>());
        services.AddScoped<IRoomService>(sp => sp.GetRequiredService<FacilityService>());
        services.AddScoped<IBedService>(sp => sp.GetRequiredService<FacilityService>());
        services.AddScoped<IFacilityService>(sp => sp.GetRequiredService<FacilityService>());
        return services;
    }
}