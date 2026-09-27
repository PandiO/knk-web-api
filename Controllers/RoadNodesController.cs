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
}
