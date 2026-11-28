using System.Security.Claims;
using HeapCare.Application.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace HeapCare.Api.Security;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddHeapCareSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection(OidcAuthenticationOptions.SectionName);

        services.AddOptions<OidcAuthenticationOptions>()
                .Bind(section)
                .Validate(options =>
                    Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority) &&
                    (authority.Scheme == Uri.UriSchemeHttps ||
                     (environment.IsDevelopment() && !options.RequireHttpsMetadata &&
                      authority.Scheme == Uri.UriSchemeHttp)),
                    "Authentication:Authority must be an absolute HTTPS URL. HTTP is allowed only in Development with explicit opt-in.")
                .Validate(options => !string.IsNullOrWhiteSpace(options.Audience),
                    "Authentication:Audience must be configured.")
                .Validate(options => options.RequireHttpsMetadata || environment.IsDevelopment(),
                    "Authentication:RequireHttpsMetadata can be disabled only in Development.")
                .ValidateOnStart();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = section["Authority"];
                options.Audience = section["Audience"];
                options.RequireHttpsMetadata =
                    section.GetValue<bool?>(nameof(OidcAuthenticationOptions.RequireHttpsMetadata)) ?? true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("HeapCare.Authentication");
                        logger.LogWarning(
                            context.Exception,
                            "Bearer token validation failed for {Path}. Token contents are intentionally not logged.",
                            context.Request.Path);
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var issuer = context.SecurityToken.Issuer;
                        var identity = context.Principal?.Identities.FirstOrDefault(candidate =>
                            candidate.IsAuthenticated);
                        var subjects = identity?.FindAll("sub").ToArray() ?? [];

                        if (string.IsNullOrWhiteSpace(issuer) ||
                            issuer.Length > 255 ||
                            identity is null ||
                            subjects.Length != 1 ||
                            string.IsNullOrWhiteSpace(subjects[0].Value) ||
                            subjects[0].Value.Length > 255)
                        {
                            context.Fail("The validated access token must contain an issuer and subject.");
                            return Task.CompletedTask;
                        }

                        // Remove legacy request-local claim names before adding trusted server claims.
                        foreach (var legacyClaimType in AuthorizationPolicies.LegacyInternalClaimTypes)
                        {
                            foreach (var legacyClaim in identity.FindAll(legacyClaimType).ToArray())
                                identity.RemoveClaim(legacyClaim);
                        }

                        // Replace any identically named claim from the token with the
                        // issuer read from the validated SecurityToken instance.
                        foreach (var existing in identity.FindAll(AuthorizationPolicies.ValidatedIssuerClaim).ToArray())
                            identity.RemoveClaim(existing);

                        identity.AddClaim(new Claim(
                            AuthorizationPolicies.ValidatedIssuerClaim,
                            issuer,
                            ClaimValueTypes.String,
                            issuer));
                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await Results.Problem(
                            type: "about:blank",
                            title: "Authentication required",
                            detail: "A valid bearer access token is required.",
                            statusCode: StatusCodes.Status401Unauthorized,
                            instance: context.Request.Path,
                            extensions: new Dictionary<string, object?>
                            {
                                ["traceId"] = context.HttpContext.TraceIdentifier
                            }).ExecuteAsync(context.HttpContext);
                    },
                    OnForbidden = async context =>
                    {
                        await Results.Problem(
                            type: "about:blank",
                            title: "Access denied",
                            detail: "The authenticated identity is not authorized for this operation.",
                            statusCode: StatusCodes.Status403Forbidden,
                            instance: context.Request.Path,
                            extensions: new Dictionary<string, object?>
                            {
                                ["traceId"] = context.HttpContext.TraceIdentifier
                            }).ExecuteAsync(context.HttpContext);
                    }
                };
            });

        services.AddAuthorization(AuthorizationPolicies.Configure);
        services.AddScoped<IAuthorizationHandler, PlatformOperatorAuthorizationHandler>();
        return services;
    }
}
