using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Item identity (PandiO/knk-workspace#141, production DESIGN §8): the material → blueprint map the game server caches
/// (any logged-in caller or the plugin key), the staff report and shadow generation (knk.admin.content), moving a
/// vanilla match (knk.admin.content), and the plugin's conversion counts (game server only).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ItemIdentityController : ControllerBase
{
    private readonly IItemIdentityService _service;

    public ItemIdentityController(IItemIdentityService service)
    {
        _service = service;
    }

    /// <summary>Every vanilla material with its one blueprint (the plugin's resolver map).</summary>
    [HttpGet("material-map")]
    public async Task<ActionResult<ItemIdentityMapDto>> GetMaterialMap(CancellationToken ct) =>
        Ok(await _service.GetMaterialMapAsync(ct));

    /// <summary>Catalogue coverage (unmatched obtainable materials, shadow vs curated matches) and conversion counts.</summary>
    [HttpGet("report")]
    [RequirePermission(StaffPermissions.ManageContent)]
    public async Task<ActionResult<ItemIdentityReportDto>> GetReport([FromQuery] int days = 7, CancellationToken ct = default) =>
        Ok(await _service.GetReportAsync(days, ct));

    /// <summary>Creates the vanilla shadow blueprints for obtainable materials without one (also runs after every scan).</summary>
    [HttpPost("shadows/generate")]
    [RequireServiceOrPermission(StaffPermissions.ManageContent)]
    public async Task<ActionResult<ItemShadowGenerationResultDto>> GenerateShadows(CancellationToken ct)
    {
        try
        {
            return Ok(await _service.GenerateShadowsAsync(ct));
        }
        catch (DbUpdateException)
        {
            return Conflict(new { code = "ConcurrentGeneration", message = "Another run created shadow blueprints at the same time; run it again." });
        }
    }

    /// <summary>Makes <c>itemBlueprintId</c> the material's one blueprint (null clears the match).</summary>
    [HttpPut("matches/{materialRefId:int}")]
    [RequireServiceOrPermission(StaffPermissions.ManageContent)]
    public async Task<ActionResult<ItemIdentityMatchResultDto>> SetMatch(int materialRefId, [FromBody] ItemIdentityMatchUpdateDto dto, CancellationToken ct)
    {
        try
        {
            return Ok(await _service.SetMatchAsync(materialRefId, dto?.ItemBlueprintId, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>The game server's conversion counts since its last report.</summary>
    [HttpPost("conversion-counts")]
    [RequirePluginService]
    public async Task<IActionResult> RecordConversionCounts([FromBody] ItemConversionCountsDto dto, CancellationToken ct)
    {
        try
        {
            var applied = await _service.RecordConversionCountsAsync(dto, ct);
            return Ok(new { applied });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
