using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace knkwebapi_v2.Configuration
{
    /// <summary>
    /// Per-client-IP rate limits on the auth endpoints (closed-alpha hardening WP6.3), with the
    /// built-in ASP.NET Core rate limiter. The client IP is the one after forwarded headers
    /// (ForwardedHeaders:Enabled), so behind a proxy that setting must be on, or every player
    /// shares the proxy's budget.
    /// </summary>
    public static class RateLimitingSetup
    {
        /// <summary>login, register, refresh, forgot-password, reset-password, validate-token.</summary>
        public const string AuthPolicy = "auth";

        /// <summary>validate-link-code, check-duplicate, link-minecraft-account.</summary>
        public const string LookupPolicy = "lookup";

        /// <summary>The public overview map endpoints, api/Map (PandiO/knk-workspace#736, O29): 120 per minute by
        /// default, room for one map polling players every 2 s (30 per minute) plus its panels.</summary>
        public const string MapPolicy = "map";

        public static IServiceCollection AddKnkRateLimiting(this IServiceCollection services, IConfiguration configuration)
        {
            var authPermit = Math.Max(1, configuration.GetValue("RateLimiting:Auth:PermitPerMinute", 10));
            var lookupPermit = Math.Max(1, configuration.GetValue("RateLimiting:Lookup:PermitPerMinute", 20));
            var mapPermit = Math.Max(1, configuration.GetValue("RateLimiting:Map:PermitPerMinute", 120));

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, cancellationToken) =>
                {
                    var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                        ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))
                        : 60;
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        error = "TooManyRequests",
                        message = "Too many requests. Wait a minute and try again."
                    }, cancellationToken);
                };
                options.AddPolicy(AuthPolicy, http => PerIp(http, AuthPolicy, authPermit));
                options.AddPolicy(LookupPolicy, http => PerIp(http, LookupPolicy, lookupPermit));
                options.AddPolicy(MapPolicy, http => PerIp(http, MapPolicy, mapPermit));
            });
            return services;
        }

        private static RateLimitPartition<string> PerIp(HttpContext http, string policy, int permitPerMinute) =>
            RateLimitPartition.GetFixedWindowLimiter($"{policy}:{http.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }
}
