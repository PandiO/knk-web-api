using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Health check endpoints for liveness and readiness probes.
/// Can be used by orchestration systems (Kubernetes, Docker Compose) or monitoring tools.
/// </summary>
[ApiController]
[Route("health")]
public class HealthCheckController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<HealthCheckController> _logger;

    public HealthCheckController(HealthCheckService healthCheckService, ILogger<HealthCheckController> logger)
    {
        _healthCheckService = healthCheckService;
        _logger = logger;
    }

    /// <summary>
    /// Liveness probe: indicates if the service is running.
    /// Always returns 200 OK if this endpoint is reachable.
    /// </summary>
    /// <response code="200">Service is running.</response>
    [HttpGet("live")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult GetLiveness()
    {
        var response = new HealthResponse
        {
            Status = "healthy",
            Timestamp = DateTime.UtcNow,
            Checks = new() { { "process", "running" } },
        };
        return Ok(response);
    }

    /// <summary>
    /// Readiness probe: indicates if the service is ready to accept traffic.
    /// Runs every registered check, including the database (KNG-115). Returns 200 when the
    /// result is healthy or degraded (degraded still serves requests) and 503 when unhealthy.
    /// The Minecraft plugin's ApiConnectivity probe reads this endpoint.
    /// </summary>
    /// <response code="200">Service is ready (status healthy or degraded).</response>
    /// <response code="503">Service or a dependency such as the database is not ready.</response>
    [HttpGet("ready")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetReadiness()
    {
        var report = await _healthCheckService.CheckHealthAsync();

        var response = new HealthResponse
        {
            Status = report.Status.ToString().ToLower(),
            Timestamp = DateTime.UtcNow,
            Checks = new(),
        };

        foreach (var entry in report.Entries)
        {
            response.Checks[entry.Key] = entry.Value.Status.ToString().ToLower();
        }

        var statusCode = report.Status == HealthStatus.Unhealthy ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK;

        if (report.Status == HealthStatus.Unhealthy)
        {
            _logger.LogWarning("Readiness check unhealthy: {Checks}",
                string.Join(", ", report.Entries.Select(e => $"{e.Key}={e.Value.Status}")));
        }

        _logger.LogDebug("Health check: {Status}, entries: {Count}", report.Status, report.Entries.Count);

        return StatusCode(statusCode, response);
    }
}

/// <summary>
/// Response model for health check endpoints.
/// </summary>
public class HealthResponse
{
    /// <summary>
    /// Overall status: "healthy", "degraded", or "unhealthy".
    /// </summary>
    public string Status { get; set; } = "healthy";

    /// <summary>
    /// Timestamp (UTC) when the health check was performed.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Dictionary of individual checks: key -> status.
    /// </summary>
    public Dictionary<string, string> Checks { get; set; } = new();

    /// <summary>
    /// Optional version or build info.
    /// </summary>
    public string? Version { get; set; }
}
