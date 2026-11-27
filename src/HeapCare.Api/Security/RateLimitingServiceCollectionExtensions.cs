using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace HeapCare.Api.Security;

public static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddHeapCareRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            // Resolve values when options are materialized, not during service
            // registration. This ensures environment/test-host configuration
            // providers added later in host construction are honoured.
            var windowSeconds = configuration.GetValue("RateLimiting:WindowSeconds", 60);
            var anonymousPermitLimit = configuration.GetValue("RateLimiting:AnonymousPermitLimit", 120);
            var authenticatedPermitLimit = configuration.GetValue("RateLimiting:AuthenticatedPermitLimit", 600);

            if (windowSeconds <= 0 || anonymousPermitLimit <= 0 || authenticatedPermitLimit <= 0)
            {
                throw new InvalidOperationException(
                    "RateLimiting:WindowSeconds, AnonymousPermitLimit and AuthenticatedPermitLimit must all be positive.");
            }

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var subject = context.User.FindFirst("sub")?.Value;
                var isAuthenticated = context.User.Identity?.IsAuthenticated == true &&
                    !string.IsNullOrWhiteSpace(subject);
                var remoteAddress = context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown";
                var partition = isAuthenticated ? $"subject:{subject}" : $"ip:{remoteAddress}";
                var permitLimit = isAuthenticated ? authenticatedPermitLimit : anonymousPermitLimit;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partition,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                            .ToString(CultureInfo.InvariantCulture);
                }

                await Results.Problem(
                    type: "https://compilecare.in/problems/rate-limit-exceeded",
                    title: "Too many requests",
                    detail: "The request rate limit has been exceeded. Retry after the indicated interval.",
                    statusCode: StatusCodes.Status429TooManyRequests,
                    instance: context.HttpContext.Request.Path,
                    extensions: new Dictionary<string, object?>
                    {
                        ["traceId"] = context.HttpContext.TraceIdentifier
                    }).ExecuteAsync(context.HttpContext);
            };
        });

        return services;
    }
}
