using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Public player profiles (KNG-34, knk-workspace docs/specs/player-statistics/IMPLEMENTATION_PLAN.md
/// §3.2). Anonymous: only the always-public fields; the viewer-filtered statistics are
/// <c>GET api/statistics/users/{userId}</c>. No online flag, email or UUID (vanish must not leak).
/// </summary>
[ApiController]
[Route("api/players")]
public class PlayersController : ControllerBase
{
    private readonly IStatisticsQueryService _statistics;

    public PlayersController(IStatisticsQueryService statistics)
    {
        _statistics = statistics;
    }

    /// <summary>A player's public profile by Minecraft/web username (case-insensitive).</summary>
    /// <response code="404">Unknown or inactive (e.g. merged) account</response>
    [HttpGet("by-name/{username}")]
    [ProducesResponseType(typeof(PublicPlayerProfileDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PublicPlayerProfileDto>> GetByName(string username, CancellationToken ct)
    {
        var profile = await _statistics.GetPublicProfileAsync(username, ct);
        return profile == null
            ? NotFound(new { error = "PlayerNotFound", message = $"No player named '{username}'." })
            : Ok(profile);
    }
}
