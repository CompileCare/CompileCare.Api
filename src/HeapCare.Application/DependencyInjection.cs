using Microsoft.Extensions.DependencyInjection;

namespace HeapCare.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<Tenancy.ITenantContext, Tenancy.TenantContext>();
        services.AddScoped<Tenancy.IOrganizationScopeContext, Tenancy.OrganizationScopeContext>();
        return services;
    }
}
