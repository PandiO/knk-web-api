using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Models.ClientActivity;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Admin endpoint for viewing client activity and metrics.
/// 
/// SECURITY NOTE: These endpoints are intended for administrators only.
/// TODO: Wire this controller to your existing authorization mechanism.
/// Currently: No auth requirement. Add [RequirePermission(StaffPermissions.ServerConfig)] when auth is in place.
/// 
/// Privacy: Metrics are aggregated by client type/id, not by individual user/player.
/// No sensitive data (player IDs, tokens, etc.) is stored or returned.
/// </summary>
[ApiController]
[Route("api/admin/clients")]
[RequirePermission(StaffPermissions.ServerConfig)]
public class AdminClientsController : ControllerBase
{
    private readonly IClientActivityStore _clientActivityStore;
    private readonly ILogger<AdminClientsController> _logger;

    public AdminClientsController(IClientActivityStore clientActivityStore, ILogger<AdminClientsController> logger)
    {
        _clientActivityStore = clientActivityStore;
        _logger = logger;
    }

    /// <summary>
    /// Gets a list of active clients with their activity snapshots.
    /// </summary>
    /// <param name="activeWithinMinutes">
    /// Only return clients active within the last N minutes. Default: 60.
    /// </param>
    /// <returns>List of client snapshots sorted by most recently seen first.</returns>
    /// <response code="200">List of clients.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ClientActivityResponse), StatusCodes.Status200OK)]
    public IActionResult GetActiveClients([FromQuery] int activeWithinMinutes = 60)
    {
        if (activeWithinMinutes <= 0)
            return BadRequest("activeWithinMinutes must be > 0");

        var timeSpan = TimeSpan.FromMinutes(activeWithinMinutes);
        var clients = _clientActivityStore.GetClients(timeSpan);

        var response = new ClientActivityResponse
        {
            TimestampUtc = DateTime.UtcNow,
            ActiveWithinMinutes = activeWithinMinutes,
            TotalActiveClients = clients.Count,
            Clients = clients.Select(ClientSnapshotDto.FromSnapshot).ToList(),
        };

        _logger.LogInformation("Admin retrieved {Count} active clients", clients.Count);

        return Ok(response);
    }

    /// <summary>
    /// Gets detailed snapshot for a specific client.
    /// </summary>
    /// <param name="clientType">Client type, e.g. "web-admin", "paper-plugin"</param>
    /// <param name="clientId">Client ID (GUID or identifier)</param>
    /// <returns>Detailed client snapshot with rolling 60-minute window data.</returns>
    /// <response code="200">Client snapshot.</response>
    /// <response code="404">Client not found or no activity recorded.</response>
    [HttpGet("{clientType}/{clientId}")]
    [ProducesResponseType(typeof(ClientSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetClient(string clientType, string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientType) || string.IsNullOrWhiteSpace(clientId))
            return BadRequest("clientType and clientId are required");

        var snapshot = _clientActivityStore.GetClient(clientType, clientId);
        if (snapshot == null)
            return NotFound($"No activity recorded for {clientType}/{clientId}");

        var dto = ClientSnapshotDto.FromSnapshot(snapshot);
        _logger.LogInformation("Admin retrieved snapshot for {ClientType}/{ClientId}", clientType, clientId);

        return Ok(dto);
    }

    /// <summary>
    /// Gets all clients, including inactive ones.
    /// </summary>
    /// <returns>All known clients sorted by most recently seen first.</returns>
    /// <response code="200">List of all clients.</response>
    [HttpGet("all")]
    [ProducesResponseType(typeof(AllClientsResponse), StatusCodes.Status200OK)]
    public IActionResult GetAllClients()
    {
        var clients = _clientActivityStore.GetAllClients();

        var response = new AllClientsResponse
        {
            TimestampUtc = DateTime.UtcNow,
            TotalClients = clients.Count,
            Clients = clients.Select(ClientSnapshotDto.FromSnapshot).ToList(),
        };

        _logger.LogInformation("Admin retrieved all {Count} clients", clients.Count);

        return Ok(response);
    }

    /// <summary>
    /// Removes clients that have been inactive for longer than the threshold.
    /// Useful for maintenance in development/testing.
    /// </summary>
    /// <param name="inactiveForHours">Remove clients inactive for longer than N hours. Default: 24.</param>
    /// <returns>Number of clients removed.</returns>
    /// <response code="200">Cleanup completed.</response>
    [HttpPost("cleanup")]
    [ProducesResponseType(typeof(CleanupResponse), StatusCodes.Status200OK)]
    public IActionResult Cleanup([FromQuery] int inactiveForHours = 24)
    {
        if (inactiveForHours <= 0)
            return BadRequest("inactiveForHours must be > 0");

        var removed = _clientActivityStore.Cleanup(TimeSpan.FromHours(inactiveForHours));

        var response = new CleanupResponse
        {
            TimestampUtc = DateTime.UtcNow,
            RemovedClientCount = removed,
            InactiveForHours = inactiveForHours,
        };

        _logger.LogInformation("Admin cleanup removed {Count} inactive clients", removed);

        return Ok(response);
    }
}

/// <summary>
/// Response for the active clients list endpoint.
/// </summary>
public class ClientActivityResponse
{
    public DateTime TimestampUtc { get; set; }
    public int ActiveWithinMinutes { get; set; }
    public int TotalActiveClients { get; set; }
    public List<ClientSnapshotDto> Clients { get; set; } = new();
}

/// <summary>
/// Response for the all clients list endpoint.
/// </summary>
public class AllClientsResponse
{
    public DateTime TimestampUtc { get; set; }
    public int TotalClients { get; set; }
    public List<ClientSnapshotDto> Clients { get; set; } = new();
}

/// <summary>
/// Response for the cleanup endpoint.
/// </summary>
public class CleanupResponse
{
    public DateTime TimestampUtc { get; set; }
    public int RemovedClientCount { get; set; }
    public int InactiveForHours { get; set; }
}

/// <summary>
/// DTO for client snapshot (used in responses).
/// </summary>
public class ClientSnapshotDto
{
    public ClientInfoDto Client { get; set; } = new();
    public DateTime LastSeenUtc { get; set; }
    public RequestInfoDto? LastRequest { get; set; }
    public long TotalRequestsLast60Min { get; set; }
    public long SuccessRequestsLast60Min { get; set; }
    public long ErrorRequestsLast60Min { get; set; }
    public double ErrorRateLast60Min { get; set; }
    public double AvgDurationMsLast60Min { get; set; }
    public List<RollingWindowBucketDto> BucketsLast60Minutes { get; set; } = new();

    public static ClientSnapshotDto FromSnapshot(ClientSnapshot snapshot)
    {
        return new ClientSnapshotDto
        {
            Client = ClientInfoDto.FromClientInfo(snapshot.Client),
            LastSeenUtc = snapshot.LastSeenUtc,
            LastRequest = snapshot.LastRequest != null ? RequestInfoDto.FromRequestInfo(snapshot.LastRequest) : null,
            TotalRequestsLast60Min = snapshot.TotalRequestsLast60Min,
            SuccessRequestsLast60Min = snapshot.SuccessRequestsLast60Min,
            ErrorRequestsLast60Min = snapshot.ErrorRequestsLast60Min,
            ErrorRateLast60Min = Math.Round(snapshot.ErrorRateLast60Min, 2),
            AvgDurationMsLast60Min = Math.Round(snapshot.AvgDurationMsLast60Min, 2),
            BucketsLast60Minutes = snapshot.BucketsLast60Minutes
                .Where(b => b != null)
                .Select(b => RollingWindowBucketDto.FromBucket(b!))
                .ToList(),
        };
    }
}

/// <summary>
/// DTO for client info.
/// </summary>
public class ClientInfoDto
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientType { get; set; } = string.Empty;
    public string? ClientVersion { get; set; }
    public string? ClientName { get; set; }

    public static ClientInfoDto FromClientInfo(ClientInfo clientInfo)
    {
        return new ClientInfoDto
        {
            ClientId = clientInfo.ClientId,
            ClientType = clientInfo.ClientType,
            ClientVersion = clientInfo.ClientVersion,
            ClientName = clientInfo.ClientName,
        };
    }
}

/// <summary>
/// DTO for request info.
/// </summary>
public class RequestInfoDto
{
    public string Method { get; set; } = string.Empty;
    public string RouteTemplate { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
    public DateTime TimestampUtc { get; set; }

    public static RequestInfoDto FromRequestInfo(RequestInfo requestInfo)
    {
        return new RequestInfoDto
        {
            Method = requestInfo.Method,
            RouteTemplate = requestInfo.RouteTemplate,
            StatusCode = requestInfo.StatusCode,
            DurationMs = requestInfo.DurationMs,
            TimestampUtc = requestInfo.TimestampUtc,
        };
    }
}

/// <summary>
/// DTO for rolling window bucket.
/// </summary>
public class RollingWindowBucketDto
{
    public int MinuteIndex { get; set; }
    public DateTime BucketStartUtc { get; set; }
    public long TotalRequests { get; set; }
    public long SuccessRequests { get; set; }
    public long ErrorRequests { get; set; }
    public double AvgDurationMs { get; set; }

    public static RollingWindowBucketDto FromBucket(RollingWindowBucket bucket)
    {
        return new RollingWindowBucketDto
        {
            MinuteIndex = bucket.MinuteIndex,
            BucketStartUtc = bucket.BucketStartUtc,
            TotalRequests = bucket.TotalRequests,
            SuccessRequests = bucket.SuccessRequests,
            ErrorRequests = bucket.ErrorRequests,
            AvgDurationMs = Math.Round(bucket.AvgDurationMs, 2),
        };
    }
}
