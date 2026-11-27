using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeapCare.Infrastructure.Persistence;

public sealed class DatabaseHealthCheck(HeapCareDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database connection is available.")
                : HealthCheckResult.Unhealthy("Database connection is unavailable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Database connection is unavailable.", exception);
        }
    }
}