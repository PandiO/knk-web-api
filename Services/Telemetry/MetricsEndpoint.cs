using System;
using System.Security.Cryptography;
using System.Text;
using knkwebapi_v2.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace knkwebapi_v2.Services.Telemetry
{
    /// <summary>
    /// The Prometheus scrape endpoint (KNG-232). It is not an MVC action, so the default-deny filter does not
    /// cover it: this class is its own gate. Disabled by default; outside Development it requires a bearer
    /// token (<see cref="Problem"/>), and the nginx/Cloudflare ingress must not route it anyway.
    /// </summary>
    public static class MetricsEndpoint
    {
        /// <summary>Why the configuration can't start, or null when it is fine.</summary>
        public static string? Problem(PrometheusExporterOptions options, bool isDevelopment)
        {
            if (!options.Enabled) return null;
            if (!options.Endpoint.StartsWith('/'))
            {
                return "Telemetry:Prometheus:Endpoint must start with '/'";
            }
            if (!isDevelopment && string.IsNullOrWhiteSpace(options.BearerToken))
            {
                return "Telemetry:Prometheus:Enabled needs Telemetry:Prometheus:BearerToken outside Development "
                    + "(set Telemetry__Prometheus__BearerToken, e.g. openssl rand -hex 32)";
            }
            return null;
        }

        /// <summary>Whether an <c>Authorization</c> header satisfies the configured token (none configured: anyone, Development only).</summary>
        public static bool IsAuthorized(string? authorizationHeader, string? token)
        {
            if (string.IsNullOrEmpty(token)) return true;
            const string prefix = "Bearer ";
            if (authorizationHeader == null || !authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var provided = Encoding.UTF8.GetBytes(authorizationHeader[prefix.Length..].Trim());
            return CryptographicOperations.FixedTimeEquals(provided, Encoding.UTF8.GetBytes(token));
        }

        public static IEndpointConventionBuilder MapKnkMetrics(this IEndpointRouteBuilder endpoints, PrometheusExporterOptions options)
        {
            return endpoints.MapPrometheusScrapingEndpoint(options.Endpoint, meterProvider: null, configureBranchedPipeline: branch => branch.Use(async (context, next) =>
            {
                if (!IsAuthorized(context.Request.Headers.Authorization, options.BearerToken))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    return;
                }
                await next();
            }), optionsName: null);
        }
    }
}
