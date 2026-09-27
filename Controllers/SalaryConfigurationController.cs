using System;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// The salary system's global multiplier (docs/specs/user-features/DESIGN.md §5), admin-editable
/// via the web app. Mirrors GameSettingsController's singleton GET/PUT shape.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SalaryConfigurationController : ControllerBase
{
    private readonly ISalaryConfigurationService _service;

    public SalaryConfigurationController(ISalaryConfigurationService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SalaryConfigurationDto), 200)]
    public async Task<ActionResult<SalaryConfigurationDto>> Get()
    {
        var config = await _service.GetAsync();
        return Ok(config);
    }

    [RequireServiceOrPermission(StaffPermissions.CurrencyPolicy)]
    [HttpPut]
    [ProducesResponseType(typeof(SalaryConfigurationDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<SalaryConfigurationDto>> Update([FromBody] UpdateSalaryConfigurationDto dto)
    {
        try
        {
            var updated = await _service.UpdateAsync(dto);
            return Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
