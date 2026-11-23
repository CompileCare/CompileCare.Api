namespace CompileCare.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    private const int MaximumLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var values = context.Request.Headers[HeaderName];
        var value = values.Count == 1 ? values[0] : null;

        if (!IsValidCorrelationId(value))
            value = Guid.NewGuid().ToString("N");

        context.TraceIdentifier = value!;
        context.Response.Headers[HeaderName] = value!;

        await next(context);
    }

    private static bool IsValidCorrelationId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumLength &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');
}
