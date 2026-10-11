using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Diagnostic telemetry (KNG-34 link 6, knk-workspace docs/specs/player-statistics/
/// IMPLEMENTATION_PLAN.md §3.3, DESIGN.md §F.12/§F.13). The plugin posts events and reads its
/// emitter config; everything else is owner-only (exact grant of a knk.owner.telemetry.* node —
/// wildcards never unlock it) and every event read is written to the audit log.
/// </summary>
[ApiController]
[Route("api/telemetry")]
public class TelemetryController : ControllerBase
{
    public const int DefaultLimit = 200;
    public const int MaxTimelineDays = 31;

    private readonly ITelemetryIngestionService _ingestion;
    private readonly ITelemetryQueryService _query;
    private readonly DiagnosticTelemetryOptions _options;

    public TelemetryController(ITelemetryIngestionService ingestion, ITelemetryQueryService query,
        IOptions<DiagnosticTelemetryOptions>? options = null)
    {
        _ingestion = ingestion;
        _query = query;
        _options = options?.Value ?? new DiagnosticTelemetryOptions();
    }

    /// <summary>
    /// Ingests up to DiagnosticTelemetry:MaxBatchSize (500) plugin events. Invalid events are listed
    /// under rejected; duplicates (by eventId) are counted, not stored twice. Valid events are queued
    /// for the background writer — the call never waits for the database. Never retry: telemetry is
    /// dropped, not spooled.
    /// </summary>
    /// <response code="200">See accepted / duplicates / dropped / rejected</response>
    /// <response code="400">Not an array, or too many events</response>
    /// <response code="401">Not the game server</response>
    /// <response code="503">DiagnosticTelemetry:Enabled is false</response>
    [RequirePluginService]
    [HttpPost("events/batch")]
    [ProducesResponseType(typeof(TelemetryBatchResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(503)]
    public async Task<ActionResult<TelemetryBatchResultDto>> PostBatch([FromBody] List<TelemetryEventDto>? events, CancellationToken ct)
    {
        if (!_options.Enabled) return Disabled();
        if (events == null) return BadRequest(new { error = "InvalidBatch", message = "The body must be an array of events." });
        if (events.Count > Math.Max(1, _options.MaxBatchSize))
        {
            return BadRequest(new { error = "TooManyEvents", message = $"At most {_options.MaxBatchSize} events per batch." });
        }
        return Ok(await _ingestion.IngestAsync(events, ct));
    }

    /// <summary>What the plugin should emit: switch, enhanced players/test runs, event names. Plugin only.</summary>
    [RequirePluginService]
    [HttpGet("config")]
    [ProducesResponseType(typeof(TelemetryClientConfigDto), 200)]
    public async Task<ActionResult<TelemetryClientConfigDto>> GetConfig(CancellationToken ct) =>
        Ok(await _query.GetClientConfigAsync(ct));

    /// <summary>Searches events, newest first (≤ 500 per page; pass nextBefore as before for older ones). Audited.</summary>
    /// <response code="400">InvalidFilter</response>
    [RequireOwnerPermission(OwnerPermissions.TelemetryView)]
    [HttpGet("events")]
    [ProducesResponseType(typeof(TelemetryEventPageDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<TelemetryEventPageDto>> Search([FromQuery] int? userId, [FromQuery] DateTime? from,
        [FromQuery] DateTime? to, [FromQuery] Guid? sessionKey, [FromQuery] int? testRunId, [FromQuery] int? matchId,
        [FromQuery] string? correlationId, [FromQuery] string? name, [FromQuery] string? outcome,
        [FromQuery] int limit = DefaultLimit, [FromQuery] string? before = null, CancellationToken ct = default)
    {
        TelemetryOutcome? parsedOutcome = null;
        if (!string.IsNullOrEmpty(outcome))
        {
            if (!Enum.TryParse<TelemetryOutcome>(outcome, true, out var o) || int.TryParse(outcome, out _))
            {
                return InvalidFilter("outcome must be succeeded, denied, failed or info.");
            }
            parsedOutcome = o;
        }
        if (limit < 1 || limit > TelemetryQueryService.MaxLimit) return InvalidFilter($"limit must be between 1 and {TelemetryQueryService.MaxLimit}.");
        if (before != null && _query.ParseCursor(before) == null) return InvalidFilter("before is not a cursor returned by this endpoint.");
        if (from != null && to != null && to <= from) return InvalidFilter("to must be after from.");

        var filter = new TelemetrySearchFilter(userId, Utc(from), Utc(to), sessionKey, testRunId, matchId,
            string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
            string.IsNullOrWhiteSpace(name) ? null : name.Trim(), parsedOutcome);
        return Ok(await _query.SearchAsync(OwnerId(), filter, before, limit, ct));
    }

    /// <summary>One event, the events sharing its correlation id, and links to ledger transactions / the Siege match. Audited.</summary>
    [RequireOwnerPermission(OwnerPermissions.TelemetryView)]
    [HttpGet("events/{eventId:guid}")]
    [ProducesResponseType(typeof(TelemetryEventDetailDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<TelemetryEventDetailDto>> GetEvent(Guid eventId, CancellationToken ct)
    {
        var detail = await _query.GetEventAsync(OwnerId(), eventId, ct);
        return detail == null
            ? NotFound(new { error = "EventNotFound", message = $"No diagnostic event {eventId}." })
            : Ok(detail);
    }

    /// <summary>
    /// A player's ordered timeline in a window (default: the last 24 h, at most 31 days): their
    /// events, ledger postings and Siege participations, joined at read time. Audited.
    /// </summary>
    [RequireOwnerPermission(OwnerPermissions.TelemetryView)]
    [HttpGet("timeline/{userId:int}")]
    [ProducesResponseType(typeof(TelemetryTimelineDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<TelemetryTimelineDto>> GetTimeline(int userId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int limit = 1000, CancellationToken ct = default)
    {
        var end = Utc(to) ?? DateTime.UtcNow;
        var start = Utc(from) ?? end.AddHours(-24);
        if (end <= start) return InvalidFilter("to must be after from.");
        if ((end - start).TotalDays > MaxTimelineDays) return InvalidFilter($"The window may span at most {MaxTimelineDays} days.");
        if (limit < 1 || limit > TelemetryQueryService.MaxLimit * 4) return InvalidFilter($"limit must be between 1 and {TelemetryQueryService.MaxLimit * 4}.");
        var timeline = await _query.GetTimelineAsync(OwnerId(), userId, start, end, limit, ct);
        return timeline == null
            ? NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" })
            : Ok(timeline);
    }

    /// <summary>Pipeline health: queue depth, drops since start, last write, events in 24 h.</summary>
    [RequireOwnerPermission(OwnerPermissions.TelemetryView)]
    [HttpGet("health")]
    [ProducesResponseType(typeof(TelemetryHealthDto), 200)]
    public async Task<ActionResult<TelemetryHealthDto>> GetHealth(CancellationToken ct) => Ok(await _query.GetHealthAsync(ct));

    /// <summary>Test runs, newest first.</summary>
    [RequireOwnerPermission(OwnerPermissions.TelemetryManage)]
    [HttpGet("test-runs")]
    [ProducesResponseType(typeof(List<TelemetryTestRunDto>), 200)]
    public async Task<ActionResult<List<TelemetryTestRunDto>>> GetTestRuns(CancellationToken ct) => Ok(await _query.GetTestRunsAsync(ct));

    /// <summary>Starts a test run (its id is stamped on events while it is active).</summary>
    [RequireOwnerPermission(OwnerPermissions.TelemetryManage)]
    [HttpPost("test-runs")]
    [ProducesResponseType(typeof(TelemetryTestRunDto), 201)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<TelemetryTestRunDto>> StartTestRun([FromBody] TelemetryTestRunCreateDto? request, CancellationToken ct)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100) return InvalidFilter("name is required (≤ 100 characters).");
        if (request!.Description?.Length > 500) return InvalidFilter("description may be at most 500 characters.");
        var run = await _query.StartTestRunAsync(OwnerId(), name, request.Description, ct);
        return StatusCode(StatusCodes.Status201Created, run);
    }

    /// <summary>Ends a test run (idempotent).</summary>
    [RequireOwnerPermission(OwnerPermissions.TelemetryManage)]
    [HttpPut("test-runs/{id:int}")]
    [ProducesResponseType(typeof(TelemetryTestRunDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<TelemetryTestRunDto>> EndTestRun(int id, CancellationToken ct)
    {
        var run = await _query.EndTestRunAsync(id, ct);
        return run == null ? NotFound(new { error = "TestRunNotFound", message = $"No test run {id}." }) : Ok(run);
    }

    /// <summary>Active enhanced-mode targets.</summary>
    [RequireOwnerPermission(OwnerPermissions.TelemetryManage)]
    [HttpGet("enhanced-targets")]
    [ProducesResponseType(typeof(List<EnhancedTargetDto>), 200)]
    public async Task<ActionResult<List<EnhancedTargetDto>>> GetEnhancedTargets(CancellationToken ct) =>
        Ok(await _query.GetEnhancedTargetsAsync(ct));

    /// <summary>Turns enhanced diagnostics on for one player or one active test run until expiresAt.</summary>
    /// <response code="400">InvalidTarget</response>
    /// <response code="404">Unknown user, or unknown/ended test run</response>
    [RequireOwnerPermission(OwnerPermissions.TelemetryManage)]
    [HttpPost("enhanced-targets")]
    [ProducesResponseType(typeof(EnhancedTargetDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<EnhancedTargetDto>> AddEnhancedTarget([FromBody] EnhancedTargetCreateDto? request, CancellationToken ct)
    {
        if (request == null || (request.UserId == null) == (request.TestRunId == null))
        {
            return BadRequest(new { error = "InvalidTarget", message = "Set exactly one of userId and testRunId." });
        }
        var now = DateTime.UtcNow;
        var expires = Utc(request.ExpiresAt)!.Value;
        if (expires <= now || expires > now.AddHours(Math.Max(1, _options.MaxEnhancedTargetHours)))
        {
            return BadRequest(new { error = "InvalidTarget", message = $"expiresAt must be in the next {_options.MaxEnhancedTargetHours} hours." });
        }
        var target = await _query.AddEnhancedTargetAsync(OwnerId(), request.UserId, request.TestRunId, expires, ct);
        return target == null
            ? NotFound(new { error = "TargetNotFound", message = "Unknown user, or the test run is unknown or ended." })
            : StatusCode(StatusCodes.Status201Created, target);
    }

    [RequireOwnerPermission(OwnerPermissions.TelemetryManage)]
    [HttpDelete("enhanced-targets/{id:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RemoveEnhancedTarget(int id, CancellationToken ct) =>
        await _query.RemoveEnhancedTargetAsync(id, ct)
            ? NoContent()
            : NotFound(new { error = "TargetNotFound", message = $"No enhanced target {id}." });

    private int OwnerId()
    {
        // RequireOwnerPermission already proved there is one.
        var caller = HttpContext.GetKnkCaller();
        return (caller.IsWebUser ? caller.WebUserId : caller.ActingUserId) ?? 0;
    }

    private ObjectResult Disabled() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "TelemetryDisabled", message = "Diagnostic telemetry is disabled (DiagnosticTelemetry:Enabled = false)." });

    private BadRequestObjectResult InvalidFilter(string message) => BadRequest(new { error = "InvalidFilter", message });

    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
    };
}
