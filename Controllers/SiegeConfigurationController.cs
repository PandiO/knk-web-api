using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Global siege tunables singleton (docs/specs/siege-minigame/DESIGN.md §3.8). Mirrors
/// SalaryConfigurationController's singleton GET/PUT shape; PUT accepts a partial body (null fields
/// keep their value). The first GET creates the row with the legacy defaults.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SiegeConfigurationController : ControllerBase
{
    private readonly ISiegeConfigurationService _service;

    public SiegeConfigurationController(ISiegeConfigurationService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SiegeConfigurationDto), 200)]
    public async Task<ActionResult<SiegeConfigurationDto>> Get()
    {
        return Ok(await _service.GetAsync());
    }

    [HttpPut]
    [ProducesResponseType(typeof(SiegeConfigurationDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<SiegeConfigurationDto>> Update([FromBody] UpdateSiegeConfigurationDto dto)
    {
        try
        {
            return Ok(await _service.UpdateAsync(dto));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
