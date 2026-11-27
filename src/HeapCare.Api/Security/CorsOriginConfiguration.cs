using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HeapCare.Api.Security;

public static class CorsOriginConfiguration
{
    public static string[] GetAllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        // Development may use HTTP, but only from explicit loopback origins.
        // Do not let an accidental Development environment turn the API into
        // a cross-origin endpoint for arbitrary remote sites.
        if (environment.IsDevelopment())
        {
            var developmentOrigins = new List<string>(configuredOrigins.Length);
            foreach (var origin in configuredOrigins)
            {
                if (!TryGetBareOrigin(origin, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                    !IsLoopbackHost(uri))
                {
                    throw new InvalidOperationException(
                        "Development Cors:AllowedOrigins entries must be explicit HTTP(S) loopback origins (localhost, *.localhost or a loopback IP) without credentials, paths, queries, fragments or wildcards.");
                }

                developmentOrigins.Add(uri.GetLeftPart(UriPartial.Authority));
            }

            return developmentOrigins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        var normalizedOrigins = new List<string>(configuredOrigins.Length);
        foreach (var origin in configuredOrigins)
        {
            if (!TryGetBareOrigin(origin, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Cors:AllowedOrigins must contain explicit HTTPS origins without credentials, paths, query strings, fragments or wildcards outside Development.");
            }

            normalizedOrigins.Add(uri.GetLeftPart(UriPartial.Authority));
        }

        return normalizedOrigins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool TryGetBareOrigin(string? origin, out Uri uri)
    {
        uri = null!;
        return !string.IsNullOrWhiteSpace(origin) &&
               string.Equals(origin, origin.Trim(), StringComparison.Ordinal) &&
               !origin.Contains('*') &&
               Uri.TryCreate(origin, UriKind.Absolute, out uri!) &&
               string.IsNullOrEmpty(uri.UserInfo) &&
               uri.AbsolutePath == "/" &&
               string.IsNullOrEmpty(uri.Query) &&
               string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool IsLoopbackHost(Uri uri)
    {
        var host = uri.IdnHost.Trim('[', ']');
        if (IPAddress.TryParse(host, out var address))
            return IPAddress.IsLoopback(address);

        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }
}
