using CompileCare.Api.Security;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore.Storage;
using Serilog.Context;

namespace CompileCare.Api.Middleware;

/// <summary>
/// Writes an append-only audit event for every tenant-scoped API request.
/// The business operation and success/denial audit record share one transaction.
/// Response bodies are buffered only for these bounded, paginated facility endpoints
/// so a persistence failure cannot return a success response for a rolled-back write.
/// </summary>
public sealed class AuditEventMiddleware(RequestDelegate next)
{
    private const string Succeeded = "Succeeded";
    private const string Denied = "Denied";
    private const string Rejected = "Rejected";
    private const string Failed = "Failed";

    public async Task InvokeAsync(
        HttpContext context,
        CompileCareDbContext db,
        ITenantContext tenantContext,
        ILogger<AuditEventMiddleware> logger)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<TenantScopedEndpointMetadata>() is null)
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        IDbContextTransaction transaction;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception,
                "Cannot start the audit transaction for tenant-scoped endpoint {Endpoint}.",
                endpoint.DisplayName);

            await WriteAuditUnavailableProblemAsync(context);
            await CopyBufferedResponseAsync(responseBuffer, originalBody);
            context.Response.Body = originalBody;
            return;
        }

        var pipelineCompleted = false;
        var responseReady = false;

        try
        {
            using (LogContext.PushProperty("AuditAction", GetAction(endpoint, context)))
            {
                await next(context);
            }

            pipelineCompleted = true;
            db.AuditEvents.Add(CreateAuditEvent(context, endpoint, tenantContext, context.Response.StatusCode));
            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            responseReady = true;
        }
        catch (Exception exception)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                logger.LogError(rollbackException,
                    "Failed to roll back the request transaction after an audit or endpoint failure.");
            }

            db.ChangeTracker.Clear();

            if (!pipelineCompleted)
            {
                await TryWriteFailureAuditEventAsync(
                    context,
                    endpoint,
                    tenantContext,
                    db,
                    logger);
                throw;
            }

            // The endpoint completed, but the audit record could not be committed.
            // The business changes have been rolled back; never return the buffered success response.
            logger.LogCritical(exception,
                "Audit persistence failed for tenant-scoped endpoint {Endpoint}; the request transaction was rolled back.",
                endpoint.DisplayName);

            await WriteAuditUnavailableProblemAsync(context);
            responseReady = true;
        }
        finally
        {
            await transaction.DisposeAsync();
            context.Response.Body = originalBody;
        }

        if (responseReady)
            await CopyBufferedResponseAsync(responseBuffer, originalBody);
    }

    private static AuditEvent CreateAuditEvent(
        HttpContext context,
        Endpoint endpoint,
        ITenantContext tenantContext,
        int statusCode)
    {
        var routeEndpoint = endpoint as RouteEndpoint;
        var routeTemplate = routeEndpoint?.RoutePattern.RawText ?? "/";
        var action = GetAction(endpoint, context);
        var tags = endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags;
        var resourceType = tags?.FirstOrDefault() ?? "Unknown";
        var resourceId = ParseRouteResourceId(context);
        var requestedTenantId = ParseRequestedTenantId(context);

        var outcome = statusCode switch
        {
            >= 500 => Failed,
            StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => Denied,
            >= 400 => Rejected,
            _ => Succeeded
        };

        return new AuditEvent
        {
            TenantId = tenantContext.TenantId,
            RequestedTenantId = requestedTenantId,
            ActorIssuer = context.User.FindFirst(AuthorizationPolicies.ValidatedIssuerClaim)?.Value,
            ActorSubject = context.User.FindFirst("sub")?.Value,
            Action = Truncate(action, 128),
            ResourceType = Truncate(resourceType, 128),
            ResourceId = resourceId,
            HttpMethod = Truncate(context.Request.Method, 10),
            RouteTemplate = Truncate(routeTemplate, 500),
            Outcome = outcome,
            StatusCode = statusCode,
            CorrelationId = Truncate(context.TraceIdentifier, 64),
            OccurredAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string GetAction(Endpoint endpoint, HttpContext context)
    {
        var action = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
        if (!string.IsNullOrWhiteSpace(action))
            return action;

        var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value ?? "/";
        return $"{context.Request.Method} {route}";
    }

    private static Guid? ParseRouteResourceId(HttpContext context) =>
        context.Request.RouteValues.TryGetValue("id", out var value) &&
        Guid.TryParse(value?.ToString(), out var id) &&
        id != Guid.Empty
            ? id
            : null;

    private static Guid? ParseRequestedTenantId(HttpContext context)
    {
        var values = context.Request.Headers[TenantContextMiddleware.HeaderName];
        return values.Count == 1 &&
               Guid.TryParse(values[0], out var tenantId) &&
               tenantId != Guid.Empty
            ? tenantId
            : null;
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static async Task TryWriteFailureAuditEventAsync(
        HttpContext context,
        Endpoint endpoint,
        ITenantContext tenantContext,
        CompileCareDbContext db,
        ILogger<AuditEventMiddleware> logger)
    {
        try
        {
            await using var failureTransaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
            db.AuditEvents.Add(CreateAuditEvent(context, endpoint, tenantContext, StatusCodes.Status500InternalServerError)
                withOutcome(Failed));
            await db.SaveChangesAsync(CancellationToken.None);
            await failureTransaction.CommitAsync(CancellationToken.None);
        }
        catch (Exception auditException)
        {
            logger.LogCritical(auditException,
                "Failed to persist the failure audit event for tenant-scoped endpoint {Endpoint}.",
                endpoint.DisplayName);
        }
    }

    private static async Task WriteAuditUnavailableProblemAsync(HttpContext context)
    {
        context.Response.Clear();
        context.Response.Headers[CorrelationIdMiddleware.HeaderName] = context.TraceIdentifier;
        await Results.Problem(
            type: "https://compilecare.in/problems/audit-unavailable",
            title: "Audit service unavailable",
            detail: "The operation could not be completed because its audit record could not be stored.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            instance: context.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = context.TraceIdentifier
            }).ExecuteAsync(context);
    }

    private static async Task CopyBufferedResponseAsync(Stream buffer, Stream destination)
    {
        buffer.Position = 0;
        await buffer.CopyToAsync(destination, CancellationToken.None);
    }
}

internal static class AuditEventExtensions
{
    public static AuditEvent withOutcome(this AuditEvent auditEvent, string outcome)
    {
        auditEvent.Outcome = outcome;
        return auditEvent;
    }
}
