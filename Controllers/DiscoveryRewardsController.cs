using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Discovery reward configuration for the web-app admin page (docs/specs/domain-discovery/DESIGN.md
/// §3.5): one rule per domain type, optional per-domain overrides, and a per-title preview.
/// </summary>
[ApiController]
[Route("api/discovery-rewards")]
[RequirePermission(StaffPermissions.ManageDiscovery)]
public class DiscoveryRewardsController : ControllerBase
{
    private readonly IDiscoveryConfigurationService _service;

    public DiscoveryRewardsController(IDiscoveryConfigurationService service)
    {
        _service = service;
    }

    /// <summary>The Town, District, Structure and GateStructure rules.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<DiscoveryRewardRuleDto>), 200)]
    public async Task<ActionResult<List<DiscoveryRewardRuleDto>>> GetRules() => Ok(await _service.GetRulesAsync());

    /// <summary>Replaces a type's rule. Every min must be at most its max; nothing negative.</summary>
    [HttpPut("{domainType}")]
    [ProducesResponseType(typeof(DiscoveryRewardRuleDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<DiscoveryRewardRuleDto>> UpdateRule(string domainType, [FromBody] UpdateDiscoveryRewardRuleDto dto)
    {
        try
        {
            return Ok(await _service.UpdateRuleAsync(domainType, dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "NotFound", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "ValidationFailed", message = ex.Message });
        }
    }

    [HttpGet("overrides")]
    [ProducesResponseType(typeof(List<DomainDiscoveryOverrideDto>), 200)]
    public async Task<ActionResult<List<DomainDiscoveryOverrideDto>>> GetOverrides() => Ok(await _service.GetOverridesAsync());

    /// <summary>Creates or replaces a domain's override; null fields inherit the type rule.</summary>
    [HttpPut("overrides/{domainId:int}")]
    [ProducesResponseType(typeof(DomainDiscoveryOverrideDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<DomainDiscoveryOverrideDto>> UpsertOverride(int domainId, [FromBody] UpdateDomainDiscoveryOverrideDto dto)
    {
        try
        {
            return Ok(await _service.UpsertOverrideAsync(domainId, dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "NotFound", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "ValidationFailed", message = ex.Message });
        }
    }

    [HttpDelete("overrides/{domainId:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteOverride(int domainId)
    {
        if (!await _service.DeleteOverrideAsync(domainId))
        {
            return NotFound(new { error = "NotFound", message = $"Domain {domainId} has no discovery override" });
        }
        return NoContent();
    }

    /// <summary>
    /// Min/max coins, gems and XP per title bracket at multiplier 1.0 for a domain type, or for
    /// one domain with its override applied.
    /// </summary>
    [HttpGet("preview")]
    [ProducesResponseType(typeof(DiscoveryRewardPreviewDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<DiscoveryRewardPreviewDto>> Preview([FromQuery] string? domainType, [FromQuery] int? domainId)
    {
        try
        {
            return Ok(await _service.PreviewAsync(domainType, domainId));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "NotFound", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "ValidationFailed", message = ex.Message });
        }
    }
}
