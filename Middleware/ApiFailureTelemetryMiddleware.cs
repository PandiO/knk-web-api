using System.Diagnostics;
using System.Text.RegularExpressions;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Telemetry;

namespace knkwebapi_v2.Middleware;

/// <summary>
/// Records an <c>api.request_failed</c> diagnostic event for every request that ends in 5xx or
/// throws (KNG-34 link 6, IMPLEMENTATION_PLAN.md §4): method, route template, status and exception
/// type only — never the path's values, query, body, headers, IP or exception message. The plugin's
/// <c>X-Correlation-Id</c> joins the failure to the in-game action that caused it. Queued without
/// waiting; never changes the response.
/// </summary>
public class ApiFailureTelemetryMiddleware
{
    public const string CorrelationHeader = "X-Correlation-Id";

    private static readonly Regex CorrelationPattern = new("^[A-Za-z0-9_.:\\-]{1,64}$", RegexOptions.Compiled);

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiFailureTelemetryMiddleware> _logger;

    public ApiFailureTelemetryMiddleware(RequestDelegate next, ILogger<ApiFailureTelemetryMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITelemetryIngestionService telemetry)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            Record(context, telemetry, StatusCodes.Status500InternalServerError, ex.GetType().Name);
            throw;
        }
        if (context.Response.StatusCode >= 500)
        {
            Record(context, telemetry, context.Response.StatusCode, null);
        }
    }

    /// <summary>The caller's correlation id when it is a stable token, else null.</summary>
    public static string? CorrelationIdOf(HttpContext context)
    {
        var header = context.Request.Headers[CorrelationHeader].ToString();
        return CorrelationPattern.IsMatch(header) ? header : null;
    }

    /// <summary>The matched route pattern (e.g. <c>api/users/{id:int}</c>); never the raw path, which
    /// may carry ids or names.</summary>
    public static string RouteTemplateOf(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is RouteEndpoint routeEndpoint && routeEndpoint.RoutePattern.RawText is { Length: > 0 } raw)
        {
            return raw.Length <= 128 ? raw : raw[..128];
        }
        return "unmatched";
    }

    private void Record(HttpContext context, ITelemetryIngestionService telemetry, int status, string? exceptionType)
    {
        try
        {
            var caller = context.GetKnkCaller();
            var userId = caller.IsWebUser ? caller.WebUserId : caller.ActingUserId;
            var route = RouteTemplateOf(context);
            var payload = new Dictionary<string, object?>
            {
                ["method"] = context.Request.Method,
                ["route"] = route,
                ["status"] = status
            };
            if (exceptionType != null) payload["exceptionType"] = exceptionType;
            telemetry.RecordApiEvent("api.request_failed", TelemetryOutcome.Failed, "request_failed", userId,
                CorrelationIdOf(context) ?? Activity.Current?.TraceId.ToString(), $"http_{status}", payload);
        }
        catch (Exception ex)
        {
            // Diagnostics must never break a response.
            _logger.LogDebug(ex, "Could not record api.request_failed");
        }
    }
}
