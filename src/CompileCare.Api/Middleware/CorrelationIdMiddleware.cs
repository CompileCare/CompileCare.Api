using Serilog.Context;

namespace CompileCare.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var value = context.Request.Headers[HeaderName].FirstOrDefault();
        if (!Guid.TryParse(value, out _)) value = Guid.NewGuid().ToString("D");

        context.TraceIdentifier = value;
        context.Response.Headers[HeaderName] = value;

        using (LogContext.PushProperty("CorrelationId", value))
        {
            await next(context);
        }
    }
}
