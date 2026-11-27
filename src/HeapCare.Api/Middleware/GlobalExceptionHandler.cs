using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HeapCare.Api.Middleware;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var isClientRequestError = exception is BadHttpRequestException badRequestException &&
            badRequestException.StatusCode is >= StatusCodes.Status400BadRequest and < StatusCodes.Status500InternalServerError;
        var statusCode = isClientRequestError
            ? ((BadHttpRequestException)exception).StatusCode
            : StatusCodes.Status500InternalServerError;

        if (isClientRequestError)
        {
            logger.LogWarning(
                exception,
                "Invalid HTTP request. StatusCode: {StatusCode}, TraceId: {TraceId}",
                statusCode,
                context.TraceIdentifier);
        }
        else
        {
            logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", context.TraceIdentifier);
        }

        context.Response.StatusCode = statusCode;

        var extensions = new Dictionary<string, object?>
        {
            ["traceId"] = context.TraceIdentifier
        };

        // Test environments expose safe diagnostic metadata only. Production and
        // deployed environments retain a generic RFC 9457 response.
        if (environment.IsEnvironment("Testing") && !isClientRequestError)
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

        var title = statusCode switch
        {
            StatusCodes.Status415UnsupportedMediaType => "Unsupported media type.",
            < StatusCodes.Status500InternalServerError => "Invalid request.",
            _ => "An unexpected error occurred."
        };

        await Results.Problem(
            title: title,
            statusCode: statusCode,
            extensions: extensions).ExecuteAsync(context);

        return true;
    }
}
