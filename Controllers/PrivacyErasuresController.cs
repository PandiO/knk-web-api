using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Privacy;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// The GDPR erasure feed for game servers (realtime S5, PandiO/knk-workspace#184, knk-workspace
/// docs/specs/realtime-signalr/DESIGN.md §7.4). The <c>AccountErased</c> hub push is at most once; the plugin calls this
/// on every hub (re)connect with the highest erasure id it has handled and deletes what it keeps about each account.
/// Game server only (the plugin service key): the rows carry former Minecraft UUIDs.
/// </summary>
[ApiController]
[Route("api/privacy/erasures")]
public class PrivacyErasuresController : ControllerBase
{
    private readonly IPrivacyDeletionService _privacy;

    public PrivacyErasuresController(IPrivacyDeletionService privacy)
    {
        _privacy = privacy;
    }

    /// <summary>Erased accounts with an erasure id above <paramref name="since"/>, oldest first.</summary>
    /// <param name="since">The highest erasure id the caller has handled (0 = from the start).</param>
    /// <param name="limit">Page size, 1-500 (default 100); <c>hasMore</c> says whether to ask again from <c>lastId</c>.</param>
    /// <response code="400">since is negative</response>
    /// <response code="401">Not the game server (no or wrong X-API-Key)</response>
    /// <response code="403">A logged-in web user</response>
    [RequirePluginService]
    [HttpGet]
    [ProducesResponseType(typeof(PrivacyErasureFeedDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<PrivacyErasureFeedDto>> GetSince([FromQuery] long since = 0, [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        if (since < 0) return BadRequest(new { error = "InvalidSince", message = "since must be 0 or a positive erasure id." });
        return Ok(await _privacy.GetErasuresSinceAsync(since, limit, ct));
    }
}
