using CompileCare.Application.Appointments;
using CompileCare.Application.Billing;
using CompileCare.Application.Clinical;
using CompileCare.Application.Facilities;
using CompileCare.Application.Patients;
using CompileCare.Application.Staff;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Appointments;
using CompileCare.Infrastructure.Billing;
using CompileCare.Infrastructure.Clinical;
using CompileCare.Infrastructure.Facilities;
using CompileCare.Infrastructure.Memberships;
using CompileCare.Infrastructure.Patients;
using CompileCare.Infrastructure.Staff;
using CompileCare.Infrastructure.Persistence;
using CompileCare.Infrastructure.Tenants;
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
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(CompileCareDbContext).Assembly.FullName));
        });

        if (!string.IsNullOrWhiteSpace(connectionString))
            services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        services.AddScoped<ITenantMembershipReader, TenantMembershipReader>();
        services.AddScoped<IPlatformOperatorReader, PlatformOperatorReader>();
        services.AddScoped<IPlatformOperatorBootstrapService, PlatformOperatorBootstrapService>();
        services.AddScoped<ITenantRegistryService, TenantRegistryService>();
        services.AddScoped<ITenantMembershipAdministrationService, TenantMembershipAdministrationService>();
        services.AddScoped<IPatientService, PatientService>();
        services.AddScoped<IPatientDuplicateDetectionService, PatientDuplicateDetectionService>();
        services.AddScoped<IPatientIdentifierService, PatientIdentifierService>();
        services.AddScoped<IStaffManagementService, StaffManagementService>();
        services.AddScoped<IAppointmentSchedulingService, AppointmentSchedulingService>();
        services.AddScoped<IAppointmentQueueService, AppointmentQueueService>();
        services.AddScoped<IOpdEncounterService, OpdEncounterService>();
        services.AddScoped<IBillingService, BillingService>();
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
