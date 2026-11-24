using CompileCare.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Persistence;

public sealed class TenantMembershipReader(CompileCareDbContext db) : ITenantMembershipReader
{
    public async Task<TenantMembershipAuthorization?> FindActiveMembershipAsync(
        Guid tenantId,
        string issuer,
        string subject,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(subject) || issuer.Length > 255 || subject.Length > 255)
            return null;

        var membership = await db.TenantMemberships
            .IgnoreQueryFilters()
            .Include(value => value.Scopes)
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.TenantId == tenantId &&
                         value.Issuer == issuer &&
                         value.Subject == subject &&
                         value.IsActive &&
                         db.Tenants.Any(tenant => tenant.Id == tenantId && tenant.Status == TenantStatuses.Active),
                cancellationToken);

        if (membership is null)
            return null;

        var scopes = membership.Scopes
            .OrderBy(scope => scope.ScopeType)
            .ThenBy(scope => scope.HospitalId)
            .ThenBy(scope => scope.DepartmentId)
            .Select(scope => new OrganizationScope(scope.ScopeType, scope.HospitalId, scope.DepartmentId))
            .ToArray();

        return new TenantMembershipAuthorization(membership.Id, membership.Role, scopes);
    }
}
