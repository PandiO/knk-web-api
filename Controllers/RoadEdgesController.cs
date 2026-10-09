using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Edge review actions and recorded edges (docs/specs/navigation/DESIGN.md §3.6, §5.10, §7): the
/// search behind the web app's edge table is anonymous, writes need the plugin key or knk.admin.road.
/// </summary>
[ApiController]
[Route("api/road-edges")]
public class RoadEdgesController : RoadControllerBase
{
    private readonly IRoadNetworkService _service;

    public RoadEdgesController(IRoadNetworkService service)
    {
        _service = service;
    }

    /// <summary>Filters: world, tileId, streetId, unlabelled (true), stale (true); sortBy id | length | streetId | tileId.</summary>
    [HttpPost("search")]
    [ProducesResponseType(typeof(PagedResultDto<RoadEdgeDto>), 200)]
    public Task<IActionResult> Search([FromBody] PagedQueryDto query) =>
        Run(async () => Ok(await _service.SearchEdgesAsync(query)));

    /// <summary>A recorded edge from an admin walk; its ends snap to nodes within 3 blocks or get Anchors.</summary>
    [HttpPost]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(typeof(RoadEdgeDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public Task<IActionResult> CreateRecorded([FromBody] RoadEdgeRecordDto dto) =>
        Run(async () => StatusCode(201, await _service.CreateRecordedEdgeAsync(dto)));

    /// <summary>Street (Manual, optionally propagated along the road), class override, cost, flags.</summary>
    [HttpPut("{id:int}")]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(typeof(RoadEdgeUpdateResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<IActionResult> Update(int id, [FromBody] RoadEdgeUpdateDto dto) =>
        Run(async () => Ok(await _service.UpdateEdgeAsync(id, dto)));

    /// <summary>
    /// Removes detected edges for good: each leaves a PrunedEdge tombstone on its centreline that
    /// every later build of its tile respects (unprune: DELETE api/road-nodes/{tombstoneId}/prune).
    /// </summary>
    [HttpPost("prune")]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(typeof(RoadEdgePruneResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<IActionResult> Prune([FromBody] RoadEdgePruneDto dto) =>
        Run(async () => Ok(await _service.PruneEdgesAsync(dto)));

    [HttpDelete("{id:int}")]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(int id) =>
        await _service.DeleteEdgeAsync(id)
            ? NoContent()
            : NotFound(new { error = "NotFound", message = $"Road edge {id} not found." });
}
