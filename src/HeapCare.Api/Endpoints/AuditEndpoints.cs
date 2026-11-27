using System.Text;
using HeapCare.Api.Security;
using HeapCare.Application.Auditing;
using Microsoft.AspNetCore.Routing;

namespace HeapCare.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/audit/events")
            .WithTags("Audit")
            .WithMetadata(TenantScopedEndpointMetadata.Instance);

        group.MapGet("/", async (
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            IAuditEventQueryService service,
            CancellationToken cancellationToken,
            string? action = null,
            string? resourceType = null,
            Guid? resourceId = null,
            int page = 1,
            int pageSize = 50) =>
        {
            var result = await service.ListAsync(
                new AuditEventQuery(fromUtc, toUtc, action, resourceType, resourceId, page, pageSize),
                cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: "Audit query rejected", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("ListAuditEvents")
        .RequireAuthorization(AuthorizationPolicies.AuditRead);

        group.MapGet("/export", async (
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            IAuditEventQueryService service,
            CancellationToken cancellationToken,
            string? action = null,
            string? resourceType = null,
            Guid? resourceId = null) =>
        {
            var result = await service.ExportCsvAsync(
                new AuditEventQuery(fromUtc, toUtc, action, resourceType, resourceId),
                cancellationToken);
            return result.IsSuccess
                ? Results.File(Encoding.UTF8.GetBytes(result.Value!), "text/csv; charset=utf-8", "compilecare-audit-events.csv")
                : Results.Problem(title: "Audit export rejected", detail: result.Error, statusCode: result.StatusCode);
        })
        .WithName("ExportAuditEvents")
        .RequireAuthorization(AuthorizationPolicies.AuditRead);

        return endpoints;
    }
}
