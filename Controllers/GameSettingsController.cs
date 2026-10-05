using System;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Global game settings singleton (docs/specs/game-settings/DESIGN.md): edited on the web app's
/// Game Settings page, read and applied by knk-plugin, which also reports its loaded worlds through
/// runtime-worlds. GET is open like the other configuration singletons; both writes need the plugin
/// key or a web user with the server-config node (KNG-52).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class GameSettingsController : ControllerBase
{
    private readonly IGameSettingsService _service;

    public GameSettingsController(IGameSettingsService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(GameSettingsReadDto), 200)]
    public async Task<ActionResult<GameSettingsReadDto>> Get()
    {
        var settings = await _service.GetAsync();
        return Ok(settings);
    }

    [RequireServiceOrPermission(StaffPermissions.ServerConfig)]
    [HttpPut]
    [ProducesResponseType(typeof(GameSettingsReadDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<GameSettingsReadDto>> Update([FromBody] GameSettingsUpdateDto dto)
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

    [RequireServiceOrPermission(StaffPermissions.ServerConfig)]
    [HttpPut("runtime-worlds")]
    [ProducesResponseType(typeof(GameSettingsReadDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<GameSettingsReadDto>> UpdateRuntimeWorlds([FromBody] GameSettingsRuntimeWorldsUpdateDto dto)
    {
        try
        {
            var updated = await _service.UpdateRuntimeWorldsAsync(dto);
            return Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
