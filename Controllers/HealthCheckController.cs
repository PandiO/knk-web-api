using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Health check endpoints for liveness and readiness probes.
/// Can be used by orchestration systems (Kubernetes, Docker Compose) or monitoring tools.
/// </summary>
[ApiController]
[Route("health")]
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
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
    /// Checks dependencies (database, etc.) and returns 200 only if all are healthy.
    /// </summary>
    /// <response code="200">Service and all dependencies are ready.</response>
    /// <response code="503">Service or dependencies are not ready.</response>
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

        var statusCode = report.Status == HealthStatus.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;

        _logger.LogInformation("Health check: {Status}, entries: {Count}", report.Status, report.Entries.Count);

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
