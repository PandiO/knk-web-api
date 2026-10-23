using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Navigation settings (KNG-73, docs/specs/navigation/DESIGN.md §6.1): where <c>/navigate &lt;domain&gt;</c>
/// leads per domain type when the player names no mode. Reads are anonymous like the road network's;
/// writes need knk.admin.road (or the plugin key). A single domain's override is a field of its own
/// form (navigationDefaultOverride on the Town/District/Structure/GateStructure DTOs). The navigation
/// compass item settings (KNG-234) live under <c>compass</c>.
/// </summary>
[ApiController]
[Route("api/navigation-settings")]
public class NavigationSettingsController : RoadControllerBase
{
    private readonly IDomainNavigationSettingsService _service;
    private readonly INavigationCompassSettingsService _compass;

    public NavigationSettingsController(IDomainNavigationSettingsService service, INavigationCompassSettingsService compass)
    {
        _service = service;
        _compass = compass;
    }

    /// <summary>Town, District, Structure and GateStructure with their default ("Spawn" or "Region").</summary>
    [HttpGet("domain-defaults")]
    [ProducesResponseType(typeof(List<DomainNavigationDefaultDto>), 200)]
    public async Task<ActionResult<List<DomainNavigationDefaultDto>>> GetDomainDefaults() =>
        Ok(await _service.GetTypeDefaultsAsync());

    [HttpPut("domain-defaults/{domainType}")]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(typeof(DomainNavigationDefaultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<IActionResult> UpdateDomainDefault(string domainType, [FromBody] UpdateDomainNavigationDefaultDto dto) =>
        Run(async () => Ok(await _service.UpdateTypeDefaultAsync(domainType, dto)));

    /// <summary>
    /// The navigation compass item rule and where it works (KNG-234): the game server binds the compass to any
    /// item whose material and display name match. Defaults while nothing was saved.
    /// </summary>
    [HttpGet("compass")]
    [ProducesResponseType(typeof(NavigationCompassSettingsDto), 200)]
    public async Task<ActionResult<NavigationCompassSettingsDto>> GetCompass() =>
        Ok(await _compass.GetAsync());

    [HttpPut("compass")]
    [RequireServiceOrPermission(StaffPermissions.RoadManage)]
    [ProducesResponseType(typeof(NavigationCompassSettingsDto), 200)]
    [ProducesResponseType(400)]
    public Task<IActionResult> UpdateCompass([FromBody] UpdateNavigationCompassSettingsDto dto) =>
        Run(async () => Ok(await _compass.UpdateAsync(dto)));
}
