using CompileCare.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Persistence;

/// <summary>
/// The only membership lookup that bypasses the ambient tenant query filter.
/// Its predicate always includes the selected tenant, validated issuer/subject, and active flag.
/// </summary>
public sealed class TenantMembershipReader(CompileCareDbContext db) : ITenantMembershipReader
{
    public async Task<TenantMembershipAuthorization?> FindActiveMembershipAsync(
        Guid tenantId,
        string issuer,
        string subject,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(subject) ||
            issuer.Length > 255 ||
            subject.Length > 255)
        {
            return null;
        }

        var role = await db.TenantMemberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(membership =>
                membership.TenantId == tenantId &&
                membership.Issuer == issuer &&
                membership.Subject == subject &&
                membership.IsActive &&
                db.Tenants.Any(tenant =>
                    tenant.Id == tenantId &&
                    tenant.Status == TenantStatuses.Active))
            .Select(membership => membership.Role)
            .SingleOrDefaultAsync(cancellationToken);

        return role is null ? null : new TenantMembershipAuthorization(role);
    }
}
