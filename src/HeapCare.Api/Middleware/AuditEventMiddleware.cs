using HeapCare.Api.Security;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore.Storage;
using Serilog.Context;

namespace HeapCare.Api.Middleware;

/// <summary>
/// Writes metadata-only audit events for tenant-scoped, user-scoped and platform-operator API requests.
/// Successful business changes and their audit event commit in one database transaction.
/// No request/response bodies, query strings, or patient payloads are persisted.
/// </summary>
public sealed class AuditEventMiddleware(RequestDelegate next)
{
    /// <summary>HttpContext.Items key for the resource ID created or targeted by a route.</summary>
    public const string ResourceIdItemKey = "HeapCare.Audit.ResourceId";

    private const string Succeeded = "Succeeded";
    private const string Denied = "Denied";
    private const string Rejected = "Rejected";
    private const string Failed = "Failed";

    public async Task InvokeAsync(
        HttpContext context,
        HeapCareDbContext db,
        ITenantContext tenantContext,
        ILogger<AuditEventMiddleware> logger)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            await next(context);
            return;
        }

        var isTenantScoped =
            endpoint.Metadata.GetMetadata<TenantScopedEndpointMetadata>() is not null;
        var isPlatformOperatorRoute =
            endpoint.Metadata.GetMetadata<PlatformOperatorEndpointMetadata>() is not null;
        var isUserScoped =
            endpoint.Metadata.GetMetadata<UserScopedEndpointMetadata>() is not null;

        if (!isTenantScoped && !isPlatformOperatorRoute && !isUserScoped)
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                exception,
                "Cannot start the audit transaction for endpoint {Endpoint}.",
                endpoint.DisplayName);

            await WriteAuditUnavailableProblemAsync(context);
            context.Response.Body = originalBody;
            await CopyBufferedResponseAsync(responseBuffer, originalBody);
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

            // The endpoint may have returned a conflict after a database exception.
            // Remove failed/pending business entity states so the audit SaveChanges
            // cannot retry them inside the transaction and poison the audit commit.
            // Successful endpoint writes are already persisted inside this transaction.
            db.ChangeTracker.Clear();

            db.AuditEvents.Add(CreateAuditEvent(
                context,
                endpoint,
                tenantContext,
                context.Response.StatusCode));

            // HeapCareDbContext and application services are scoped: the endpoint's
            // SaveChanges and this audit insert participate in the same transaction.
            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            responseReady = true;
        }
        catch (Exception exception)
        {
            // The exception text is emitted to the configured protected application
            // log, never to the API response or audit event payload.
            logger.LogError(
                exception,
                "Audited endpoint {Endpoint} failed before its response completed.",
                endpoint.DisplayName);

            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                logger.LogError(
                    rollbackException,
                    "Failed to roll back the audited request transaction.");
            }
            finally
            {
                await transaction.DisposeAsync();
                transaction = null;
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

            // The endpoint completed but the audit insert/commit failed. Its business
            // writes are rolled back, and the buffered success response is discarded.
            logger.LogCritical(
                exception,
                "Audit persistence failed for endpoint {Endpoint}; the request transaction was rolled back.",
                endpoint.DisplayName);

            responseBuffer.SetLength(0);
            responseBuffer.Position = 0;
            await WriteAuditUnavailableProblemAsync(context);
            responseReady = true;
        }
        finally
        {
            if (transaction is not null)
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
            RequestedTenantId = ParseRequestedTenantId(context),
            ActorIssuer = context.User.FindFirst(AuthorizationPolicies.ValidatedIssuerClaim)?.Value,
            ActorSubject = context.User.FindFirst("sub")?.Value,
            Action = Truncate(action, 128),
            ResourceType = Truncate(resourceType, 128),
            ResourceId = ParseRouteResourceId(context),
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

        var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "/";
        return $"{context.Request.Method} {route}";
    }

    public static Guid? ParseRouteResourceId(HttpContext context)
    {
        // Create routes may not contain the newly created resource ID in their URL.
        // Let the endpoint provide that ID explicitly after a successful write; this
        // value takes precedence over parent identifiers such as hospitalId.
        if (context.Items.TryGetValue(ResourceIdItemKey, out var explicitResource) &&
            explicitResource is Guid explicitResourceId &&
            explicitResourceId != Guid.Empty)
        {
            return explicitResourceId;
        }

        // Route values are already matched and typed by ASP.NET routing. The last
        // GUID parameter in the route template identifies the target resource for
        // both conventional {id} routes and named routes such as {encounterId},
        // {queueEntryId}, and {patientId}. Parent hospital IDs therefore do not
        // mask the more specific resource identifier.
        if (context.GetEndpoint() is not RouteEndpoint routeEndpoint)
            return null;

        foreach (var parameter in routeEndpoint.RoutePattern.Parameters.Reverse())
        {
            if (!context.Request.RouteValues.TryGetValue(parameter.Name, out var value))
                continue;

            if (Guid.TryParse(value?.ToString(), out var resourceId) && resourceId != Guid.Empty)
                return resourceId;
        }

        return null;
    }

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
        HeapCareDbContext db,
        ILogger<AuditEventMiddleware> logger)
    {
        try
        {
            await using var failureTransaction =
                await db.Database.BeginTransactionAsync(CancellationToken.None);

            var failureEvent = CreateAuditEvent(
                context,
                endpoint,
                tenantContext,
                StatusCodes.Status500InternalServerError);
            failureEvent.Outcome = Failed;
            db.AuditEvents.Add(failureEvent);

            await db.SaveChangesAsync(CancellationToken.None);
            await failureTransaction.CommitAsync(CancellationToken.None);
        }
        catch (Exception auditException)
        {
            logger.LogCritical(
                auditException,
                "Failed to persist the failure audit event for endpoint {Endpoint}.",
                endpoint.DisplayName);
        }
    }

    private static async Task WriteAuditUnavailableProblemAsync(HttpContext context)
    {
        context.Response.Clear();
        context.Response.Headers[CorrelationIdMiddleware.HeaderName] = context.TraceIdentifier;

        await Results.Problem(
            type: "about:blank",
            title: "Service Unavailable",
            detail: "The operation could not be completed because its audit record could not be stored.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            instance: context.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "audit_unavailable",
                ["traceId"] = context.TraceIdentifier
            }).ExecuteAsync(context);
    }

    private static async Task CopyBufferedResponseAsync(Stream buffer, Stream destination)
    {
        buffer.Position = 0;
        await buffer.CopyToAsync(destination, CancellationToken.None);
    }
}
