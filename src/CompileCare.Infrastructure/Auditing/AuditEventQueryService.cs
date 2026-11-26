using System.Text;
using CompileCare.Application.Auditing;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Auditing;

public sealed class AuditEventQueryService(
    CompileCareDbContext db,
    ITenantContext tenantContext,
    IOrganizationScopeContext organizationScopeContext) : IAuditEventQueryService
{
    private const int MaximumPageSize = 100;
    private const int MaximumExportRows = 5_000;
    private static readonly TimeSpan MaximumQueryRange = TimeSpan.FromDays(90);
    private static readonly TimeSpan MaximumExportRange = TimeSpan.FromDays(31);

    public async Task<AuditEventQueryResult<AuditEventPage>> ListAsync(
        AuditEventQuery query, CancellationToken cancellationToken)
    {
        var validation = Validate(query, MaximumQueryRange);
        if (validation is not null)
            return AuditEventQueryResult<AuditEventPage>.Failure(validation, 400);
        if (!organizationScopeContext.IsTenantWide)
            return AuditEventQueryResult<AuditEventPage>.Failure("Audit access requires a tenant-wide administrator scope.", 403);
        if (tenantContext.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            return AuditEventQueryResult<AuditEventPage>.Failure("A validated tenant context is required.", 403);

        var filtered = ApplyFilters(db.AuditEvents.AsNoTracking(), tenantId, query);
        var totalCount = await filtered.CountAsync(cancellationToken);
        var page = query.Page < 1 ? 1 : Math.Min(query.Page, int.MaxValue / MaximumPageSize);
        var pageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, MaximumPageSize);
        var items = await filtered
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new AuditEventDto(
                item.Id, item.TenantId, item.RequestedTenantId, item.ActorIssuer, item.ActorSubject,
                item.Action, item.ResourceType, item.ResourceId, item.HttpMethod, item.RouteTemplate,
                item.Outcome, item.StatusCode, item.CorrelationId, item.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        return AuditEventQueryResult<AuditEventPage>.Success(new(items, page, pageSize, totalCount));
    }

    public async Task<AuditEventQueryResult<string>> ExportCsvAsync(
        AuditEventQuery query, CancellationToken cancellationToken)
    {
        var validation = Validate(query, MaximumExportRange);
        if (validation is not null)
            return AuditEventQueryResult<string>.Failure(validation, 400);
        if (!organizationScopeContext.IsTenantWide)
            return AuditEventQueryResult<string>.Failure("Audit export requires a tenant-wide administrator scope.", 403);
        if (tenantContext.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            return AuditEventQueryResult<string>.Failure("A validated tenant context is required.", 403);

        var filtered = ApplyFilters(db.AuditEvents.AsNoTracking(), tenantId, query);
        var count = await filtered.CountAsync(cancellationToken);
        if (count > MaximumExportRows)
            return AuditEventQueryResult<string>.Failure(
                $"The export exceeds {MaximumExportRows} records. Narrow the date range or filters.", 400);

        var items = await filtered
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenByDescending(item => item.Id)
            .Select(item => new AuditEventDto(
                item.Id, item.TenantId, item.RequestedTenantId, item.ActorIssuer, item.ActorSubject,
                item.Action, item.ResourceType, item.ResourceId, item.HttpMethod, item.RouteTemplate,
                item.Outcome, item.StatusCode, item.CorrelationId, item.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine("Id,TenantId,RequestedTenantId,ActorIssuer,ActorSubject,Action,ResourceType,ResourceId,HttpMethod,RouteTemplate,Outcome,StatusCode,CorrelationId,OccurredAtUtc");
        foreach (var item in items)
        {
            csv.Append(Csv(item.Id.ToString("D"))).Append(',')
                .Append(Csv(item.TenantId?.ToString("D"))).Append(',')
                .Append(Csv(item.RequestedTenantId?.ToString("D"))).Append(',')
                .Append(Csv(item.ActorIssuer)).Append(',')
                .Append(Csv(item.ActorSubject)).Append(',')
                .Append(Csv(item.Action)).Append(',')
                .Append(Csv(item.ResourceType)).Append(',')
                .Append(Csv(item.ResourceId?.ToString("D"))).Append(',')
                .Append(Csv(item.HttpMethod)).Append(',')
                .Append(Csv(item.RouteTemplate)).Append(',')
                .Append(Csv(item.Outcome)).Append(',')
                .Append(Csv(item.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append(',')
                .Append(Csv(item.CorrelationId)).Append(',')
                .Append(Csv(item.OccurredAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)))
                .AppendLine();
        }

        return AuditEventQueryResult<string>.Success(csv.ToString());
    }

    private static IQueryable<AuditEvent> ApplyFilters(
        IQueryable<AuditEvent> query, Guid tenantId, AuditEventQuery filter)
    {
        query = query.Where(item =>
            item.TenantId == tenantId &&
            item.OccurredAtUtc >= filter.FromUtc &&
            item.OccurredAtUtc < filter.ToUtc);
        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(item => item.Action == filter.Action.Trim());
        if (!string.IsNullOrWhiteSpace(filter.ResourceType))
            query = query.Where(item => item.ResourceType == filter.ResourceType.Trim());
        if (filter.ResourceId.HasValue)
            query = query.Where(item => item.ResourceId == filter.ResourceId.Value);
        return query;
    }

    private static string? Validate(AuditEventQuery query, TimeSpan maximumRange)
    {
        if (query.FromUtc == default || query.ToUtc == default || query.FromUtc >= query.ToUtc)
            return "A valid UTC start and exclusive end time are required.";
        if (query.FromUtc.Offset != TimeSpan.Zero || query.ToUtc.Offset != TimeSpan.Zero)
            return "Audit query times must be UTC.";
        if (query.ToUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            return "The audit query end time cannot be in the future.";
        if (query.ToUtc - query.FromUtc > maximumRange)
            return $"The requested time range cannot exceed {maximumRange.TotalDays:0} days.";
        if (query.Action?.Length > 128 || query.ResourceType?.Length > 128)
            return "Audit action and resource type filters cannot exceed 128 characters.";
        return null;
    }

    private static string Csv(string? value)
    {
        var safe = value ?? string.Empty;
        if (safe.Length > 0 && (safe[0] is '=' or '+' or '-' or '@'))
            safe = $"'{safe}";
        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
