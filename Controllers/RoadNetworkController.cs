using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Per-world road network metadata (docs/specs/navigation/DESIGN.md §3.8): the profiles, the
/// street names edges are labelled with and the component summary; plus the domain Locations
/// the builder seeds from (plan D12).
/// </summary>
[ApiController]
[Route("api/road-network")]
public class RoadNetworkController : RoadControllerBase
{
    private readonly IRoadNetworkService _service;

    public RoadNetworkController(IRoadNetworkService service)
    {
        _service = service;
    }

    [HttpGet("meta")]
    [ProducesResponseType(typeof(RoadNetworkMetaDto), 200)]
    [ProducesResponseType(400)]
    public Task<IActionResult> GetMeta([FromQuery] string world) =>
        Run(async () => Ok(await _service.GetMetaAsync(world)));

    /// <summary>Town/District/Structure/Gate Locations inside the x/z box (inclusive) of a world.</summary>
    [HttpGet("seed-locations")]
    [ProducesResponseType(typeof(List<RoadSeedLocationDto>), 200)]
    [ProducesResponseType(400)]
    public Task<IActionResult> GetSeedLocations([FromQuery] string world, [FromQuery] int minX, [FromQuery] int minZ, [FromQuery] int maxX, [FromQuery] int maxZ) =>
        Run(async () => Ok(await _service.GetSeedLocationsAsync(world, minX, minZ, maxX, maxZ)));
}
