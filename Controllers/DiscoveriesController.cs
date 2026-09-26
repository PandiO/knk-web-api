using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Domain discovery: the plugin's grant call, a player's discoveries, and admin statistics
/// (docs/specs/domain-discovery/DESIGN.md §3.5). Reward rules and overrides live in
/// DiscoveryRewardsController.
/// </summary>
[ApiController]
public class DiscoveriesController : ControllerBase
{
    private readonly IDiscoveryService _service;

    public DiscoveriesController(IDiscoveryService service)
    {
        _service = service;
    }

    /// <summary>
    /// Discovers and rewards the domains behind the given WorldGuard region ids and/or domain ids
    /// (plus their ancestors per the type rules). Called by knk-plugin. Idempotent: a repeat grants
    /// nothing and lists the domains under alreadyDiscovered. Amounts are computed server-side.
    /// </summary>
    /// <response code="200">Evaluated - see granted / alreadyDiscovered / skipped</response>
    /// <response code="400">No ids, more than 50, or an unknown source</response>
    /// <response code="401">Not the game server (no or wrong X-API-Key)</response>
    /// <response code="403">A logged-in web user - only the game server grants discoveries</response>
    /// <response code="404">User not found</response>
    [RequirePluginService]
    [HttpPost("api/users/{userId:int}/discoveries")]
    [ProducesResponseType(typeof(DiscoveryGrantResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<DiscoveryGrantResultDto>> Grant(int userId, [FromBody] DiscoveryGrantRequestDto request)
    {
        try
        {
            return Ok(await _service.DiscoverAsync(userId, request));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "ValidationFailed", message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" });
        }
    }

    /// <summary>Every domain the user has discovered with its region id, for the plugin's cache.</summary>
    [RequirePluginService]
    [HttpGet("api/users/{userId:int}/discoveries/known")]
    [ProducesResponseType(typeof(List<KnownDiscoveryDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<List<KnownDiscoveryDto>>> GetKnown(int userId)
    {
        try
        {
            return Ok(await _service.GetKnownAsync(userId));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" });
        }
    }

    /// <summary>
    /// Every enabled discoverable domain with the user's discovered state. Filters: "domainType"
    /// (Town/District/Structure/GateStructure), "status" (discovered/undiscovered); searchTerm on
    /// the name; sortBy "name" or "discoveredAt" (default: Town, District, Structure, then name).
    /// The game server, the player themself (their account page) or staff with knk.admin.discovery.
    /// </summary>
    [RequireServiceSelfOrPermission(StaffPermissions.ManageDiscovery)]
    [HttpPost("api/users/{userId:int}/discoveries/progress")]
    [ProducesResponseType(typeof(PagedResultDto<DiscoveryProgressRowDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PagedResultDto<DiscoveryProgressRowDto>>> GetProgress(int userId, [FromBody] PagedQueryDto? query)
    {
        try
        {
            return Ok(await _service.GetProgressAsync(userId, query ?? new PagedQueryDto()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" });
        }
    }

    /// <summary>Discovered vs. total per type, the latest discovery and lifetime reward totals. The
    /// game server, the player themself or staff with knk.admin.discovery.</summary>
    [RequireServiceSelfOrPermission(StaffPermissions.ManageDiscovery)]
    [HttpGet("api/users/{userId:int}/discoveries/summary")]
    [ProducesResponseType(typeof(DiscoverySummaryDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<DiscoverySummaryDto>> GetSummary(int userId)
    {
        try
        {
            return Ok(await _service.GetSummaryAsync(userId));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" });
        }
    }

    /// <summary>
    /// Resets one discovery so the domain can be discovered and rewarded again (no claw-back),
    /// audited as DiscoveryReset. Staff only: an open reset would let anyone farm a domain's reward
    /// by resetting and re-entering it. The game server may call it for /knk discovery reset (its
    /// own node check), naming the staff member in X-Acting-User-Id.
    /// </summary>
    /// <response code="204">Reset</response>
    /// <response code="404">User not found, or the user hadn't discovered that domain</response>
    [RequireServiceOrPermission(StaffPermissions.ManageDiscovery)]
    [HttpDelete("api/users/{userId:int}/discoveries/{domainId:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Reset(int userId, int domainId)
    {
        try
        {
            if (!await _service.ResetAsync(userId, domainId, HttpContext.GetKnkCaller().ActorUserId))
            {
                return NotFound(new { error = "NotFound", message = $"User {userId} has not discovered domain {domainId}" });
            }
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" });
        }
    }

    /// <summary>
    /// Discoverers per enabled domain (filter by domainType; sortBy "discoverers" (default) or
    /// "name"; sortDescending=true for most discovered first), the linked-account count the
    /// percentages are taken over, and the top 10 explorers.
    /// </summary>
    [RequirePermission(StaffPermissions.ManageDiscovery)]
    [HttpGet("api/discoveries/stats")]
    [ProducesResponseType(typeof(DiscoveryStatsDto), 200)]
    public async Task<ActionResult<DiscoveryStatsDto>> GetStats(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? domainType = null,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = true)
    {
        var query = new PagedQueryDto
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            SearchTerm = searchTerm,
            SortBy = sortBy,
            SortDescending = sortDescending,
            Filters = string.IsNullOrWhiteSpace(domainType) ? null : new Dictionary<string, string> { ["domainType"] = domainType }
        };
        return Ok(await _service.GetStatsAsync(query));
    }
}
