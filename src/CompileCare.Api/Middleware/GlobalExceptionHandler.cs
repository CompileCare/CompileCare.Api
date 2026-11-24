using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

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

        // Test environments expose safe diagnostic metadata only. Production and
        // deployed environments retain the generic RFC 9457 response.
        if (environment.IsEnvironment("Testing"))
        {
            extensions["diagnostic"] = $"{exception.GetType().Name}: {exception.Message}";
            if (exception is DbUpdateConcurrencyException concurrencyException)
            {
                extensions["concurrencyEntries"] = concurrencyException.Entries
                    .Select(entry => new
                    {
                        Entity = entry.Metadata.ClrType.Name,
                        State = entry.State.ToString(),
                        Key = string.Join(
                            ",",
                            entry.Metadata.FindPrimaryKey()?.Properties.Select(property => property.Name)
                            ?? Array.Empty<string>())
                    })
                    .ToArray();
            }
        }

        await Results.Problem(
            title: "An unexpected error occurred.",
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: extensions).ExecuteAsync(context);

        return true;
    }
}
