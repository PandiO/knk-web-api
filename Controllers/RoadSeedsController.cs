using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>Admin and survey seeds the road mask grows from (docs/specs/navigation/DESIGN.md §3.4).</summary>
[ApiController]
[Route("api/road-seeds")]
public class RoadSeedsController : RoadControllerBase
{
    private readonly IRoadNetworkService _service;

    public RoadSeedsController(IRoadNetworkService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<RoadSeedDto>), 200)]
    [ProducesResponseType(400)]
    public Task<IActionResult> List([FromQuery] string world) =>
        Run(async () => Ok(await _service.ListSeedsAsync(world)));

    [HttpPost]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(typeof(RoadSeedDto), 201)]
    [ProducesResponseType(400)]
    public Task<IActionResult> Create([FromBody] RoadSeedCreateDto dto) =>
        Run(async () => StatusCode(201, await _service.CreateSeedAsync(dto)));

    [HttpDelete("{id:int}")]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(int id) =>
        await _service.DeleteSeedAsync(id)
            ? NoContent()
            : NotFound(new { error = "NotFound", message = $"Road seed {id} not found." });
}
