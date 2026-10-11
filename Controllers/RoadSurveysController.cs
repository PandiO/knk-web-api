using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Finished survey walks (docs/specs/navigation/DESIGN.md §3.2, §5.3), stored by the plugin. The
/// walking staff member comes from X-Acting-User-Id.
/// </summary>
[ApiController]
[Route("api/road-surveys")]
public class RoadSurveysController : RoadControllerBase
{
    private readonly IRoadNetworkService _service;

    public RoadSurveysController(IRoadNetworkService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<RoadSurveyDto>), 200)]
    [ProducesResponseType(400)]
    public Task<IActionResult> List([FromQuery] string world) =>
        Run(async () => Ok(await _service.ListSurveysAsync(world)));

    [HttpPost]
    [RequirePluginService]
    [ProducesResponseType(typeof(RoadSurveyDto), 201)]
    [ProducesResponseType(400)]
    public Task<IActionResult> Create([FromBody] RoadSurveyCreateDto dto) => Run(async () =>
    {
        var created = await _service.CreateSurveyAsync(dto, HttpContext.GetKnkCaller().ActorUserId);
        return StatusCode(201, created);
    });
}
