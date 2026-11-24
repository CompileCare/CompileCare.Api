using Microsoft.AspNetCore.Diagnostics;

namespace CompileCare.Api.Middleware;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var extensions = new Dictionary<string, object?>
        {
            ["traceId"] = context.TraceIdentifier
        };

        // Test environments return the exception type/message to make integration
        // failures diagnosable. Production and other deployed environments keep a
        // generic RFC 9457 response and never expose internal exception details.
        if (environment.IsEnvironment("Testing"))
            extensions["diagnostic"] = $"{exception.GetType().Name}: {exception.Message}";

        await Results.Problem(
            title: "An unexpected error occurred.",
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: extensions).ExecuteAsync(context);

        return true;
    }
}
