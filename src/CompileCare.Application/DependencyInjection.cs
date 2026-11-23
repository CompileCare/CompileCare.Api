using Microsoft.Extensions.DependencyInjection;
namespace CompileCare.Application;
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<Tenancy.ITenantContext, Tenancy.TenantContext>();
        return services;
    }
}
