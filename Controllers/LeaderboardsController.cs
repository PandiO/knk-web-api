using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Leaderboards;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Leaderboards (KNG-34, knk-workspace docs/specs/player-statistics/IMPLEMENTATION_PLAN.md §3.2,
/// DESIGN.md §F.11). Boards are served from precomputed snapshots. The viewer (JWT user, or plugin
/// + X-Acting-User-Id) gets their own row; anonymous visitors may read always-public boards only
/// (L1-3: a player's "everyone" means signed-in viewers). Exclusions are owner-only (exact grant).
/// </summary>
[ApiController]
[Route("api/leaderboards")]
public class LeaderboardsController : ControllerBase
{
    public const int DefaultTop = 10;
    public const int MaxTop = 50;

    private readonly ILeaderboardQueryService _leaderboards;

    public LeaderboardsController(ILeaderboardQueryService leaderboards)
    {
        _leaderboards = leaderboards;
    }

    /// <summary>Every board with its periods. Public.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LeaderboardBoardDto>), 200)]
    public ActionResult<IReadOnlyList<LeaderboardBoardDto>> GetBoards() => Ok(_leaderboards.GetBoards());

    /// <summary>A board's current ranking: top N (≤ 50) plus the viewer's own position.</summary>
    /// <param name="boardKey"><c>&lt;metric&gt;</c> or <c>&lt;metric&gt;@&lt;context&gt;</c>.</param>
    /// <param name="period">weekly, monthly or lifetime (default).</param>
    /// <param name="top">Entries to return (1-50, default 10).</param>
    /// <param name="ct">Cancellation.</param>
    /// <response code="400">InvalidPeriod or InvalidTop</response>
    /// <response code="401">SignInRequired — the board ranks a configurable metric</response>
    /// <response code="404">UnknownBoard</response>
    [HttpGet("{boardKey}")]
    [ProducesResponseType(typeof(LeaderboardViewDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<LeaderboardViewDto>> GetBoard(string boardKey, [FromQuery] string? period,
        [FromQuery] int top = DefaultTop, CancellationToken ct = default)
    {
        var board = LeaderboardCatalog.Find(boardKey);
        if (board == null) return NotFound(new { error = "UnknownBoard", message = $"Unknown leaderboard '{boardKey}'." });
        var parsed = period == null ? LeaderboardPeriod.Lifetime : LeaderboardCatalog.ParsePeriod(period);
        if (parsed == null) return BadRequest(new { error = "InvalidPeriod", message = "period must be weekly, monthly or lifetime." });
        if (top < 1 || top > MaxTop) return BadRequest(new { error = "InvalidTop", message = $"top must be between 1 and {MaxTop}." });

        var caller = HttpContext.GetKnkCaller();
        var viewerId = caller.IsWebUser ? caller.WebUserId : caller.IsPluginService ? caller.ActingUserId : null;
        if (viewerId == null && !board.AlwaysPublic)
        {
            return Unauthorized(new { error = "SignInRequired", message = "Sign in to see this leaderboard." });
        }
        return Ok(await _leaderboards.GetBoardAsync(board, parsed.Value, top, viewerId, ct));
    }

    /// <summary>Players excluded from every leaderboard. Owner only.</summary>
    [RequireOwnerPermission(OwnerPermissions.LeaderboardManage)]
    [HttpGet("exclusions")]
    [ProducesResponseType(typeof(List<LeaderboardExclusionDto>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<List<LeaderboardExclusionDto>>> GetExclusions(CancellationToken ct) =>
        Ok(await _leaderboards.GetExclusionsAsync(ct));

    /// <summary>Excludes a player from every leaderboard from the next refresh. Owner only.</summary>
    [RequireOwnerPermission(OwnerPermissions.LeaderboardManage)]
    [HttpPut("exclusions/{userId:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> PutExclusion(int userId, [FromBody] LeaderboardExclusionRequestDto? request, CancellationToken ct)
    {
        var caller = HttpContext.GetKnkCaller();
        var ownerId = caller.IsWebUser ? caller.WebUserId : caller.ActingUserId;
        return await _leaderboards.ExcludeAsync(userId, request?.Reason, ownerId ?? 0, ct) ? NoContent() : UserNotFound(userId);
    }

    /// <summary>Lifts a player's exclusion from the next refresh. Owner only.</summary>
    [RequireOwnerPermission(OwnerPermissions.LeaderboardManage)]
    [HttpDelete("exclusions/{userId:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteExclusion(int userId, CancellationToken ct) =>
        await _leaderboards.IncludeAsync(userId, ct) ? NoContent() : UserNotFound(userId);

    private NotFoundObjectResult UserNotFound(int userId) =>
        NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" });
}
