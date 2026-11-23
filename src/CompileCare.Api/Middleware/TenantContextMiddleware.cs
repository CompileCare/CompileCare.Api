using CompileCare.Application.Tenancy;
using Serilog.Context;

namespace CompileCare.Api.Middleware;

public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Tenant-ID";

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var raw = context.Request.Headers[HeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (!Guid.TryParse(raw, out var tenantId) || tenantId == Guid.Empty)
            {
                await Results.Problem(title: "Invalid tenant context", detail: $"{HeaderName} must be a non-empty GUID.", statusCode: 400).ExecuteAsync(context);
                return;
            }

            tenantContext.SetTenant(tenantId);
            using (LogContext.PushProperty("TenantId", tenantId))
            {
                await next(context);
            }

            return;
        }

        await next(context);
    }
}