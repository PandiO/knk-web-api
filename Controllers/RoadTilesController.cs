using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Road tiles (docs/specs/navigation/DESIGN.md §3.3, §3.8; plan D3/D4): the per-world overview,
/// the per-tile graph download with an ETag (the tile Version), the plugin's build-result upsert,
/// the dirty mark, and the curated-tile state and proposals (plan §5.7).
/// </summary>
[ApiController]
[Route("api/road-tiles")]
public class RoadTilesController : RoadControllerBase
{
    private readonly IRoadNetworkService _service;

    public RoadTilesController(IRoadNetworkService service)
    {
        _service = service;
    }

    /// <summary>Every tile of a world with its version, so the plugin can compare with its cache.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<RoadTileDto>), 200)]
    [ProducesResponseType(400)]
    public Task<IActionResult> List([FromQuery] string world) =>
        Run(async () => Ok(await _service.ListTilesAsync(world)));

    /// <summary>
    /// One tile's nodes and the edges it owns. Responds with <c>ETag: "&lt;version&gt;"</c>; a request
    /// whose <c>If-None-Match</c> carries that version gets 304 with no body.
    /// </summary>
    [HttpGet("{world}/{tileX:int}/{tileZ:int}/graph")]
    [ProducesResponseType(typeof(RoadTileGraphDto), 200)]
    [ProducesResponseType(304)]
    [ProducesResponseType(404)]
    public Task<IActionResult> GetGraph(string world, int tileX, int tileZ) => Run(async () =>
    {
        var graph = await _service.GetTileGraphAsync(world, tileX, tileZ);
        if (graph == null)
        {
            return NotFound(new { error = "NotFound", message = $"Tile ({tileX}, {tileZ}) of world '{world}' has not been built." });
        }
        var etag = ETagOf(graph.Tile.Version);
        Response.Headers.ETag = etag;
        if (Request.Headers.IfNoneMatch.Any(value => Matches(value, etag)))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }
        return Ok(graph);
    });

    /// <summary>The plugin's build result for one tile (DESIGN §3.8): one transaction, idempotent.</summary>
    [HttpPut("{world}/{tileX:int}/{tileZ:int}/graph")]
    [RequirePluginService]
    [ProducesResponseType(typeof(RoadTileUpsertResultDto), 200)]
    [ProducesResponseType(400)]
    public Task<IActionResult> PutGraph(string world, int tileX, int tileZ, [FromBody] RoadTileGraphUpsertDto dto) =>
        Run(async () => Ok(await _service.UpsertTileGraphAsync(world, tileX, tileZ, dto)));

    /// <summary>Marks a tile dirty (DESIGN §5.9): its edges become Stale, still routable.</summary>
    [HttpPost("{world}/{tileX:int}/{tileZ:int}/dirty")]
    [RequirePluginService]
    [ProducesResponseType(typeof(RoadTileDto), 200)]
    [ProducesResponseType(400)]
    public Task<IActionResult> MarkDirty(string world, int tileX, int tileZ) =>
        Run(async () => Ok(await _service.MarkTileDirtyAsync(world, tileX, tileZ)));

    /// <summary>Plan §5.7, D1: Curated (builds make proposals) or Detected (the next build is uploaded
    /// directly and curates the tile again).</summary>
    [HttpPut("{world}/{tileX:int}/{tileZ:int}/state")]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(typeof(RoadTileDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<IActionResult> PutState(string world, int tileX, int tileZ, [FromBody] RoadTileStateDto dto) =>
        Run(async () => Ok(await _service.SetTileStateAsync(world, tileX, tileZ, dto)));

    /// <summary>Every tile of the world that has a proposal row (pending items or a rejected list).</summary>
    [HttpGet("proposals")]
    [ProducesResponseType(typeof(List<RoadTileProposalSummaryDto>), 200)]
    [ProducesResponseType(400)]
    public Task<IActionResult> ListProposals([FromQuery] string world) =>
        Run(async () => Ok(await _service.ListProposalsAsync(world)));

    /// <summary>The tile's proposal: pending items and the rejected list (plan §5.7, D5).</summary>
    [HttpGet("{world}/{tileX:int}/{tileZ:int}/proposal")]
    [ProducesResponseType(typeof(RoadTileProposalDto), 200)]
    [ProducesResponseType(404)]
    public Task<IActionResult> GetProposal(string world, int tileX, int tileZ) => Run(async () =>
    {
        var proposal = await _service.GetProposalAsync(world, tileX, tileZ);
        return proposal == null
            ? NotFound(new { error = "NotFound", message = $"Tile ({tileX}, {tileZ}) of world '{world}' has no proposal." })
            : Ok(proposal);
    });

    /// <summary>Replaces the tile's proposal; the plugin computes it from a rebuild of a Curated tile.</summary>
    [HttpPut("{world}/{tileX:int}/{tileZ:int}/proposal")]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(typeof(RoadTileProposalDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> PutProposal(string world, int tileX, int tileZ, [FromBody] RoadTileProposalUpsertDto dto) =>
        Run(async () => Ok(await _service.SaveProposalAsync(world, tileX, tileZ, dto)));

    /// <summary>Deletes the tile's proposal row, the rejected list included.</summary>
    [HttpDelete("{world}/{tileX:int}/{tileZ:int}/proposal")]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public Task<IActionResult> DeleteProposal(string world, int tileX, int tileZ) => Run(async () =>
        await _service.DeleteProposalAsync(world, tileX, tileZ)
            ? NoContent()
            : NotFound(new { error = "NotFound", message = $"Tile ({tileX}, {tileZ}) of world '{world}' has no proposal." }));

    public static string ETagOf(int version) => $"\"{version}\"";

    private static bool Matches(string? headerValue, string etag)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }
        return headerValue.Split(',')
            .Select(v => v.Trim())
            .Select(v => v.StartsWith("W/", System.StringComparison.Ordinal) ? v[2..] : v)
            .Any(v => v == etag || v == "*");
    }
}
