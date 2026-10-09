using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>Node review actions (docs/specs/navigation/DESIGN.md §3.5, §7): rename/re-kind/lock, anchors, merges.</summary>
[ApiController]
[Route("api/road-nodes")]
[RequireServiceOrPermission(StaffPermissions.RoadManage)]
public class RoadNodesController : RoadControllerBase
{
    private readonly IRoadNetworkService _service;

    public RoadNodesController(IRoadNetworkService service)
    {
        _service = service;
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(RoadNodeDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<IActionResult> Update(int id, [FromBody] RoadNodeUpdateDto dto) =>
        Run(async () => Ok(await _service.UpdateNodeAsync(id, dto)));

    /// <summary>A manual Anchor node: the builder splits the road there (DESIGN §5.6).</summary>
    [HttpPost("anchor")]
    [ProducesResponseType(typeof(RoadNodeDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public Task<IActionResult> CreateAnchor([FromBody] RoadNodeAnchorDto dto) =>
        Run(async () => StatusCode(201, await _service.CreateAnchorAsync(dto)));

    /// <summary>Moves mergeNodeId's edges to keepNodeId and deletes it (a junction detected twice).</summary>
    [HttpPost("merge")]
    [ProducesResponseType(typeof(RoadNodeDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<IActionResult> Merge([FromBody] RoadNodeMergeDto dto) =>
        Run(async () => Ok(await _service.MergeNodesAsync(dto)));

    /// <summary>
    /// Removes the dead end ending at endpoint {id} and keeps a Pruned tombstone there, so rebuilds
    /// leave that arm out (a junction left with two arms dissolves).
    /// </summary>
    [HttpPost("{id:int}/prune")]
    [ProducesResponseType(typeof(RoadNodeDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Prune(int id) =>
        Run(async () => Ok(await _service.PruneNodeAsync(id)));

    /// <summary>Deletes the Pruned or PrunedEdge tombstone {id}: the next build brings the arm or edge back.</summary>
    [HttpDelete("{id:int}/prune")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<IActionResult> Unprune(int id) =>
        Run(async () => await _service.UnpruneNodeAsync(id)
            ? NoContent()
            : NotFound(new { error = "NotFound", message = $"Road node {id} not found." }));
}
