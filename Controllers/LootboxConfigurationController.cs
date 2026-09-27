using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// The lootbox singleton settings (docs/specs/lootboxes/DESIGN.md §3.2-§3.3), admin-editable via the web app.
/// Mirrors SalaryConfigurationController's singleton GET/PUT shape. The plugin reads these through the Phase 2
/// runtime-config endpoint, not here.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[RequirePermission(StaffPermissions.ManageLootboxes)]
public class LootboxConfigurationController : ControllerBase
{
    private readonly ILootboxConfigurationService _service;

    public LootboxConfigurationController(ILootboxConfigurationService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(LootboxConfigurationDto), 200)]
    public async Task<ActionResult<LootboxConfigurationDto>> Get()
    {
        return Ok(await _service.GetAsync());
    }

    [HttpPut]
    [ProducesResponseType(typeof(LootboxConfigurationDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<LootboxConfigurationDto>> Update([FromBody] UpdateLootboxConfigurationDto dto)
    {
        if (dto == null) return BadRequest();
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
