using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Statistics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Player statistics (KNG-34, knk-workspace docs/specs/player-statistics/IMPLEMENTATION_PLAN.md
/// §3.1): the plugin's batch ingestion, the catalogue, viewer-filtered reads and the player's
/// visibility settings. Read routes take no auth attribute on purpose: the viewer (JWT user,
/// plugin + X-Acting-User-Id, or anonymous) is resolved per request and the API applies the
/// player's visibility settings to every response.
/// </summary>
[ApiController]
[Route("api/statistics")]
public class StatisticsController : ControllerBase
{
    private readonly IStatisticsIngestionService _ingestion;
    private readonly IStatisticsQueryService _query;
    private readonly IStatisticsVisibilityService _visibility;
    private readonly IStatisticsViewerResolver _viewers;
    private readonly StatisticsOptions _options;
    private readonly IStatisticsRebuildService? _rebuild;

    public StatisticsController(IStatisticsIngestionService ingestion, IStatisticsQueryService query,
        IStatisticsVisibilityService visibility, IStatisticsViewerResolver viewers, IOptions<StatisticsOptions>? options = null,
        IStatisticsRebuildService? rebuild = null)
    {
        _ingestion = ingestion;
        _query = query;
        _visibility = visibility;
        _viewers = viewers;
        _options = options?.Value ?? new StatisticsOptions();
        _rebuild = rebuild;
    }

    /// <summary>
    /// Deletes and recomputes the ledger and/or Siege projections (economy, XP gained, title history;
    /// match results) for one player or for everyone (KNG-34 link 6, IMPLEMENTATION_PLAN.md §4).
    /// Owner only (exact grant of knk.owner.telemetry.manage). Runs in the request — for everyone it
    /// re-projects the whole ledger, so prefer a userId.
    /// </summary>
    /// <response code="200">Rebuilt; reprojected = source records re-projected</response>
    /// <response code="400">InvalidProjection (ledger, siege or all)</response>
    /// <response code="401">No signed-in owner</response>
    /// <response code="403">No exact grant of knk.owner.telemetry.manage</response>
    /// <response code="503">Statistics:Enabled is false</response>
    [RequireOwnerPermission(OwnerPermissions.TelemetryManage)]
    [HttpPost("rebuild")]
    [ProducesResponseType(typeof(StatisticsRebuildResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(503)]
    public async Task<ActionResult<StatisticsRebuildResultDto>> Rebuild([FromBody] StatisticsRebuildRequestDto? request, CancellationToken ct)
    {
        if (!_options.Enabled || _rebuild == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "StatisticsDisabled", message = "Statistics are disabled (Statistics:Enabled = false)." });
        }
        var projection = (request?.Projection ?? "all").Trim().ToLowerInvariant();
        if (projection is not ("ledger" or "siege" or "all"))
        {
            return BadRequest(new { error = "InvalidProjection", message = "projection must be ledger, siege or all." });
        }
        var count = await _rebuild.RebuildAsync(projection, request?.UserId, ct);
        return Ok(new StatisticsRebuildResultDto { Projection = projection, UserId = request?.UserId, Reprojected = count });
    }

    /// <summary>
    /// Ingests one plugin flush. Idempotent per batchId (a replay answers duplicate: true and changes
    /// nothing). Invalid entries are listed under rejected with a code; the rest is applied.
    /// </summary>
    /// <response code="200">Applied (see accepted / rejected), or a duplicate</response>
    /// <response code="400">No batchId or too many entries — final, don't retry</response>
    /// <response code="401">Not the game server (no or wrong X-API-Key)</response>
    /// <response code="403">A logged-in web user — only the game server reports statistics</response>
    /// <response code="503">Statistics:Enabled is false — keep the batch spooled</response>
    [RequirePluginService]
    [HttpPost("batches")]
    [ProducesResponseType(typeof(StatisticsBatchResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(503)]
    public async Task<ActionResult<StatisticsBatchResultDto>> PostBatch([FromBody] StatisticsBatchDto batch, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "StatisticsDisabled", message = "Statistics ingestion is disabled (Statistics:Enabled = false)." });
        }
        try
        {
            return Ok(await _ingestion.IngestAsync(batch, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "InvalidBatch", message = ex.Message });
        }
    }

    /// <summary>Metric keys, settings, groups, contexts and the period time zone. Public.</summary>
    [HttpGet("catalog")]
    [ProducesResponseType(typeof(StatisticsCatalogDto), 200)]
    public ActionResult<StatisticsCatalogDto> GetCatalog() => Ok(_query.GetCatalog());

    /// <summary>A player's statistics for a period, filtered for the caller.</summary>
    /// <param name="userId">The player.</param>
    /// <param name="period">lifetime (default), day, week or month.</param>
    /// <param name="date">A local day inside the wanted period (default today).</param>
    /// <param name="ct">Cancellation.</param>
    [HttpGet("users/{userId:int}")]
    [ProducesResponseType(typeof(PlayerStatisticsDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<ActionResult<PlayerStatisticsDto>> GetUser(int userId, [FromQuery] string? period, [FromQuery] DateOnly? date,
        CancellationToken ct) =>
        Run(userId, viewer => _query.GetAsync(userId, viewer, period, date, ct));

    /// <summary>One metric per day, week or month (≤ 366 points); 403 when the caller may not see it.</summary>
    [HttpGet("users/{userId:int}/series")]
    [ProducesResponseType(typeof(StatisticSeriesDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public Task<ActionResult<StatisticSeriesDto>> GetSeries(int userId, [FromQuery] string? metric, [FromQuery] string? context,
        [FromQuery] string? granularity, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Run(userId, viewer => _query.GetSeriesAsync(userId, viewer, metric, context, granularity, from, to, ct));

    /// <summary>Title promotions and demotions, newest first (setting title_history).</summary>
    [HttpGet("users/{userId:int}/title-history")]
    [ProducesResponseType(typeof(PagedResultDto<TitleChangeDto>), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public Task<ActionResult<PagedResultDto<TitleChangeDto>>> GetTitleHistory(int userId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Run(userId, viewer => _query.GetTitleHistoryAsync(userId, viewer, page, pageSize, ct));

    /// <summary>Named discovered places, newest first (setting discoveries.list).</summary>
    [HttpGet("users/{userId:int}/discoveries")]
    [ProducesResponseType(typeof(PagedResultDto<DiscoveryListItemDto>), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public Task<ActionResult<PagedResultDto<DiscoveryListItemDto>>> GetDiscoveries(int userId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Run(userId, viewer => _query.GetDiscoveriesAsync(userId, viewer, page, pageSize, ct));

    /// <summary>The player's visibility settings: the player themselves, or staff with
    /// knk.admin.statistics.view (read-only).</summary>
    [HttpGet("users/{userId:int}/visibility")]
    [ProducesResponseType(typeof(StatisticsVisibilityDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<StatisticsVisibilityDto>> GetVisibility(int userId, CancellationToken ct)
    {
        var viewer = await _viewers.ResolveAsync(HttpContext.GetKnkCaller(), userId);
        if (viewer.Kind == StatisticsViewerKind.Anonymous) return Unauthorized(new { error = "Unauthorized", message = "Log in to use this." });
        if (!viewer.SeesEverything) return Forbidden("Only the player themselves (or staff, read-only) can see these settings.");
        var dto = await _visibility.GetAsync(userId, ct);
        return dto == null ? UserNotFound(userId) : Ok(dto);
    }

    /// <summary>
    /// Changes visibility settings atomically (the player themselves only). Every change carries the
    /// value the client showed; on any mismatch nothing changes and the 409 body carries the current
    /// settings.
    /// </summary>
    /// <response code="400">UnknownSetting, NotContextual, InvalidContext, DuplicateChange, TooManyChanges</response>
    /// <response code="409">VisibilityConflict — body: { error, message, current }</response>
    [HttpPut("users/{userId:int}/visibility")]
    [ProducesResponseType(typeof(StatisticsVisibilityDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<StatisticsVisibilityDto>> PutVisibility(int userId, [FromBody] StatisticsVisibilityUpdateDto update,
        CancellationToken ct)
    {
        var viewer = await _viewers.ResolveAsync(HttpContext.GetKnkCaller(), userId);
        if (viewer.Kind == StatisticsViewerKind.Anonymous) return Unauthorized(new { error = "Unauthorized", message = "Log in to use this." });
        if (viewer.Kind != StatisticsViewerKind.Self) return Forbidden("Only the player themselves can change these settings.");
        try
        {
            var dto = await _visibility.UpdateAsync(userId, update, ct);
            return dto == null ? UserNotFound(userId) : Ok(dto);
        }
        catch (StatisticsValidationException ex)
        {
            return BadRequest(new { error = ex.Code, message = ex.Message });
        }
        catch (StatisticsVisibilityConflictException ex)
        {
            return Conflict(new { error = "VisibilityConflict", message = ex.Message, current = ex.Current });
        }
    }

    private async Task<ActionResult<T>> Run<T>(int userId, Func<StatisticsViewer, Task<T?>> read) where T : class
    {
        var viewer = await _viewers.ResolveAsync(HttpContext.GetKnkCaller(), userId);
        try
        {
            var result = await read(viewer);
            return result == null ? UserNotFound(userId) : Ok(result);
        }
        catch (StatisticsValidationException ex)
        {
            return BadRequest(new { error = ex.Code, message = ex.Message });
        }
        catch (StatisticsHiddenException ex)
        {
            return Forbidden(ex.Message);
        }
    }

    private ObjectResult Forbidden(string message) =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden", message });

    private NotFoundObjectResult UserNotFound(int userId) =>
        NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" });
}
