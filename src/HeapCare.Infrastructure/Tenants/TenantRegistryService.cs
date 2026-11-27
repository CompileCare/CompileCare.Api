using System.Text.RegularExpressions;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HeapCare.Infrastructure.Tenants;

public sealed class TenantRegistryService(HeapCareDbContext db) : ITenantRegistryService
{
    private static readonly Regex SlugPattern =
        new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<TenantPage<TenantDto>> ListAsync(TenantPageRequest page, CancellationToken cancellationToken)
    {
        var query = db.Tenants.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(value => value.Name).ThenBy(value => value.Id)
            .Skip(page.Skip).Take(page.SafePageSize)
            .Select(value => new TenantDto(value.Id, value.Name, value.Slug, value.Status, value.CreatedAtUtc, value.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
        return new TenantPage<TenantDto>(items, page.SafePage, page.SafePageSize, totalCount);
    }

    public async Task<TenantRegistryResult<TenantDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
            return Failure<TenantDto>("A non-empty tenant identifier is required.", 400);

        var tenant = await db.Tenants.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        return tenant is null
            ? Failure<TenantDto>("Tenant not found.", 404)
            : TenantRegistryResult<TenantDto>.Success(ToDto(tenant));
    }

    public async Task<TenantRegistryResult<TenantDto>> CreateAsync(
        TenantCreateInput input,
        CancellationToken cancellationToken)
    {
        var name = input.Name?.Trim();
        var slug = input.Slug?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Failure<TenantDto>("Name is required and must not exceed 200 characters.", 400);
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 63 || !SlugPattern.IsMatch(slug))
            return Failure<TenantDto>("Slug must contain lowercase letters or digits separated by single hyphens, up to 63 characters.", 400);

        var tenant = new Tenant { Name = name, Slug = slug, Status = TenantStatuses.Provisioning };
        db.Tenants.Add(tenant);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Failure<TenantDto>("A tenant with this slug already exists.", 409);
        }

        return TenantRegistryResult<TenantDto>.Success(ToDto(tenant));
    }

    public async Task<TenantRegistryResult<TenantDto>> TransitionAsync(
        Guid id,
        string targetStatus,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
            return Failure<TenantDto>("A non-empty tenant identifier is required.", 400);
        if (!TenantStatuses.IsKnown(targetStatus))
            return Failure<TenantDto>("The requested tenant status is not supported.", 400);

        var tenant = await db.Tenants.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (tenant is null)
            return Failure<TenantDto>("Tenant not found.", 404);

        if (string.Equals(tenant.Status, targetStatus, StringComparison.Ordinal))
            return TenantRegistryResult<TenantDto>.Success(ToDto(tenant));
        if (!TenantStatuses.CanTransition(tenant.Status, targetStatus))
            return Failure<TenantDto>($"Tenant status cannot transition from '{tenant.Status}' to '{targetStatus}'.", 409);

        if (tenant.Status == TenantStatuses.Provisioning && targetStatus == TenantStatuses.Active)
        {
            var hasInitialAdministrator = await db.TenantMemberships.IgnoreQueryFilters()
                .AnyAsync(value =>
                    value.TenantId == id &&
                    value.Role == TenantRoles.HospitalAdmin &&
                    value.IsActive &&
                    value.Scopes.Any(scope => scope.ScopeType == OrganizationScopeTypes.Tenant),
                    cancellationToken);
            if (!hasInitialAdministrator)
                return Failure<TenantDto>(
                    "A tenant-wide HospitalAdmin must be provisioned before the tenant can be activated.",
                    409);
        }

        tenant.Status = targetStatus;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure<TenantDto>("Tenant changed concurrently. Reload and retry.", 409);
        }

        return TenantRegistryResult<TenantDto>.Success(ToDto(tenant));
    }

    private static TenantDto ToDto(Tenant tenant) =>
        new(tenant.Id, tenant.Name, tenant.Slug, tenant.Status, tenant.CreatedAtUtc, tenant.UpdatedAtUtc);

    private static TenantRegistryResult<T> Failure<T>(string error, int statusCode) =>
        TenantRegistryResult<T>.Failure(error, statusCode);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
