using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Road profiles (docs/specs/navigation/DESIGN.md §3.1, §5.1): reads are anonymous; writes come
/// from the plugin (ProfileLearner after a survey, plan D5) or a staff member holding
/// knk.admin.road (plan D1).
/// </summary>
[ApiController]
[Route("api/road-profiles")]
public class RoadProfilesController : RoadControllerBase
{
    private readonly IRoadNetworkService _service;

    public RoadProfilesController(IRoadNetworkService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<RoadProfileDto>), 200)]
    public async Task<ActionResult<List<RoadProfileDto>>> List() => Ok(await _service.ListProfilesAsync());

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(RoadProfileDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(int id)
    {
        var profile = await _service.GetProfileAsync(id);
        return profile == null ? NotFound(new { error = "NotFound", message = $"Road profile {id} not found." }) : Ok(profile);
    }

    [HttpPost]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(typeof(RoadProfileDto), 201)]
    [ProducesResponseType(400)]
    public Task<IActionResult> Create([FromBody] RoadProfileUpsertDto dto) => Run(async () =>
    {
        var created = await _service.CreateProfileAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    });

    [HttpPut("{id:int}")]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(typeof(RoadProfileDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<IActionResult> Update(int id, [FromBody] RoadProfileUpsertDto dto) =>
        Run(async () => Ok(await _service.UpdateProfileAsync(id, dto)));

    [HttpDelete("{id:int}")]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(int id) =>
        await _service.DeleteProfileAsync(id)
            ? NoContent()
            : NotFound(new { error = "NotFound", message = $"Road profile {id} not found." });
}
