using Microsoft.AspNetCore.Diagnostics;
namespace CompileCare.Api.Middleware;
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = 500;
        await Results.Problem(title: "An unexpected error occurred.", statusCode: 500,
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        return true;
    }
}
