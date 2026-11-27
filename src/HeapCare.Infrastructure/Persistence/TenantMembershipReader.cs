using HeapCare.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HeapCare.Infrastructure.Persistence;

public sealed class TenantMembershipReader(HeapCareDbContext db) : ITenantMembershipReader
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
    public async Task<TenantMembershipPage<UserTenantContextDto>> ListActiveTenantContextsAsync(
        string issuer,
        string subject,
        TenantMembershipPageRequest page,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(issuer) || issuer.Length > 255 ||
            string.IsNullOrWhiteSpace(subject) || subject.Length > 255)
        {
            return new TenantMembershipPage<UserTenantContextDto>(
                Array.Empty<UserTenantContextDto>(), page.SafePage, page.SafePageSize, 0);
        }

        var memberships = db.TenantMemberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(value =>
                value.Issuer == issuer &&
                value.Subject == subject &&
                value.IsActive &&
                db.Tenants.IgnoreQueryFilters().Any(tenant =>
                    tenant.Id == value.TenantId &&
                    tenant.Status == TenantStatuses.Active));

        var totalCount = await memberships.CountAsync(cancellationToken);
        var pageMemberships = await memberships
            .Include(value => value.Scopes)
            .OrderBy(value => value.TenantId)
            .ThenBy(value => value.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .ToListAsync(cancellationToken);

        if (pageMemberships.Count == 0)
        {
            return new TenantMembershipPage<UserTenantContextDto>(
                Array.Empty<UserTenantContextDto>(), page.SafePage, page.SafePageSize, totalCount);
        }

        var tenantIds = pageMemberships.Select(value => value.TenantId).Distinct().ToArray();
        var tenants = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(value => tenantIds.Contains(value.Id) && value.Status == TenantStatuses.Active)
            .Select(value => new { value.Id, value.Name, value.Slug })
            .ToDictionaryAsync(value => value.Id, cancellationToken);

        var items = pageMemberships
            .Where(value => tenants.ContainsKey(value.TenantId))
            .Select(value =>
            {
                var tenant = tenants[value.TenantId];
                var scopes = value.Scopes
                    .OrderBy(scope => scope.ScopeType)
                    .ThenBy(scope => scope.HospitalId)
                    .ThenBy(scope => scope.DepartmentId)
                    .Select(scope => new OrganizationScope(
                        scope.ScopeType, scope.HospitalId, scope.DepartmentId))
                    .ToArray();

                return new UserTenantContextDto(
                    value.TenantId, tenant.Name, tenant.Slug, value.Role, scopes);
            })
            .ToArray();

        return new TenantMembershipPage<UserTenantContextDto>(
            items, page.SafePage, page.SafePageSize, totalCount);
    }

}
