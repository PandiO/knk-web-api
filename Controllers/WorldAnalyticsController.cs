using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.WorldAnalytics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// World analytics (KNG-34 link 7, knk-workspace docs/specs/player-statistics/
/// IMPLEMENTATION_PLAN.md §3.4, DESIGN.md D10/D11): the plugin posts anonymous per-window aggregates
/// (movement cells, menu funnel steps, domain interactions); every read is owner-only (exact grant of
/// knk.owner.analytics.view — wildcards never unlock it). Ranges are inclusive local days
/// (yyyy-MM-dd, Statistics:TimeZone), default the last 7 days.
/// </summary>
[ApiController]
[Route("api/world-analytics")]
public class WorldAnalyticsController : ControllerBase
{
    private readonly IWorldAnalyticsIngestionService _ingestion;
    private readonly IWorldAnalyticsQueryService _query;
    private readonly WorldAnalyticsOptions _options;

    public WorldAnalyticsController(IWorldAnalyticsIngestionService ingestion, IWorldAnalyticsQueryService query,
        IOptions<WorldAnalyticsOptions>? options = null)
    {
        _ingestion = ingestion;
        _query = query;
        _options = options?.Value ?? new WorldAnalyticsOptions();
    }

    /// <summary>
    /// Ingests one flush window of aggregates (≤ WorldAnalytics:MaxBatchRows rows). A batch id seen
    /// before applies nothing (duplicate). Invalid rows are listed under rejected with a code.
    /// </summary>
    /// <response code="200">Applied (see accepted / rejected), or a duplicate</response>
    /// <response code="400">No batchId/windowStart, window out of range or too many rows — final, don't retry</response>
    /// <response code="401">Not the game server</response>
    /// <response code="503">WorldAnalytics:Enabled is false — the plugin drops the batch</response>
    [RequirePluginService]
    [HttpPost("batches")]
    [ProducesResponseType(typeof(WorldAnalyticsBatchResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(503)]
    public async Task<ActionResult<WorldAnalyticsBatchResultDto>> PostBatch([FromBody] WorldAnalyticsBatchDto batch, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "WorldAnalyticsDisabled", message = "World analytics ingestion is disabled (WorldAnalytics:Enabled = false)." });
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

    /// <summary>Worlds with movement samples in the range, busiest first, with their stored cell sizes.</summary>
    [RequireOwnerPermission(OwnerPermissions.AnalyticsView)]
    [HttpGet("heatmap/worlds")]
    [ProducesResponseType(typeof(List<HeatmapWorldDto>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<List<HeatmapWorldDto>>> GetHeatmapWorlds([FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        if (!TryRange(from, to, out var range, out var error)) return error!;
        return Ok(await _query.GetWorldsAsync(range.From, range.To, ct));
    }

    /// <summary>
    /// Movement samples per cell of one world (cellSize = any multiple of a stored size; default the
    /// smallest stored). At most WorldAnalytics:MaxHeatmapCells cells, busiest first.
    /// </summary>
    [RequireOwnerPermission(OwnerPermissions.AnalyticsView)]
    [HttpGet("heatmap")]
    [ProducesResponseType(typeof(HeatmapDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<HeatmapDto>> GetHeatmap([FromQuery] string? world, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, [FromQuery] int? cellSize, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(world)) return InvalidFilter("world is required.");
        if (!TryRange(from, to, out var range, out var error)) return error!;
        try
        {
            return Ok(await _query.GetHeatmapAsync(world, range.From, range.To, cellSize, ct));
        }
        catch (ArgumentException ex)
        {
            return InvalidFilter(ex.Message);
        }
    }

    /// <summary>Menu steps (opened, actions by outcome, back, closed) per menu, busiest menu first.</summary>
    [RequireOwnerPermission(OwnerPermissions.AnalyticsView)]
    [HttpGet("menu-funnels")]
    [ProducesResponseType(typeof(MenuFunnelReportDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<MenuFunnelReportDto>> GetMenuFunnels([FromQuery] string? menuKey, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!TryRange(from, to, out var range, out var error)) return error!;
        return Ok(await _query.GetMenuFunnelsAsync(menuKey, range.From, range.To, ct));
    }

    /// <summary>Entries, exits, discoveries and visitor counts per domain, most entries first.</summary>
    [RequireOwnerPermission(OwnerPermissions.AnalyticsView)]
    [HttpGet("domains")]
    [ProducesResponseType(typeof(DomainInteractionReportDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<DomainInteractionReportDto>> GetDomains([FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? kind, CancellationToken ct)
    {
        if (!TryRange(from, to, out var range, out var error)) return error!;
        try
        {
            return Ok(await _query.GetDomainsAsync(kind, range.From, range.To, ct));
        }
        catch (ArgumentException ex)
        {
            return InvalidFilter(ex.Message);
        }
    }

    private bool TryRange(DateOnly? from, DateOnly? to, out (DateOnly From, DateOnly To) range, out ActionResult? error)
    {
        try
        {
            range = _query.ResolveRange(from, to);
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            range = default;
            error = InvalidFilter(ex.Message);
            return false;
        }
    }

    private BadRequestObjectResult InvalidFilter(string message) => BadRequest(new { error = "InvalidFilter", message });
}
