using knkwebapi_v2.Attributes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
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
    private readonly IHubSettingsService _hub;

    public GameSettingsController(IGameSettingsService service, IHubSettingsService hub)
    {
        _service = service;
        _hub = hub;
    }

    /// <summary>The hub-eligible domains (hub DESIGN §16.4 decision 4), the selected one marked.</summary>
    [RequireServiceOrPermission(StaffPermissions.ServerConfig)]
    [HttpGet("hub/domains")]
    [ProducesResponseType(typeof(List<HubDomainDto>), 200)]
    public async Task<ActionResult<List<HubDomainDto>>> GetHubDomains()
    {
        return Ok(await _hub.ListEligibleAsync());
    }

    /// <summary>Flags or unflags a domain as hub-eligible; 400 for nested hubs or unflagging the selected hub.</summary>
    [RequireServiceOrPermission(StaffPermissions.ServerConfig)]
    [HttpPut("hub/domains/{domainId:int}")]
    [ProducesResponseType(typeof(HubDomainDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<HubDomainDto>> SetHubEligible(int domainId, [FromBody] HubEligibilityUpdateDto dto)
    {
        try
        {
            var updated = await _hub.SetEligibleAsync(domainId, dto?.IsHub == true);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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
