using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CompileCare.Api.Security;

public static class CorsOriginConfiguration
{
    public static string[] GetAllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        // Development may use loopback HTTP. Production CORS entries must be
        // explicit HTTPS origins; paths, credentials, queries, fragments and
        // wildcard hosts are not valid CORS origins for this application.
        if (environment.IsDevelopment())
            return configuredOrigins;

        var normalizedOrigins = new List<string>(configuredOrigins.Length);
        foreach (var origin in configuredOrigins)
        {
            if (string.IsNullOrWhiteSpace(origin) ||
                !string.Equals(origin, origin.Trim(), StringComparison.Ordinal) ||
                origin.Contains('*') ||
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidOperationException(
                    "Cors:AllowedOrigins must contain explicit HTTPS origins without credentials, paths, query strings, fragments or wildcards outside Development.");
            }

            normalizedOrigins.Add(uri.GetLeftPart(UriPartial.Authority));
        }

        return normalizedOrigins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
