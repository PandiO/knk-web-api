using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Maintenance of the Minecraft catalogue (KNG-200, docs/specs/minecraft-catalog/IMPLEMENTATION_PLAN.md
/// §8): audit report, backfill and the merge action. Staff with knk.admin.content only, reads included.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[RequirePermission(StaffPermissions.ManageContent)]
public class MinecraftCatalogController : ControllerBase
{
    private readonly IMinecraftCatalogMaintenanceService _maintenance;

    public MinecraftCatalogController(IMinecraftCatalogMaintenanceService maintenance)
    {
        _maintenance = maintenance;
    }

    [HttpGet("audit")]
    public async Task<ActionResult<MinecraftCatalogAuditDto>> Audit(CancellationToken ct)
    {
        return Ok(await _maintenance.AuditAsync(ct));
    }

    /// <summary>Runs the backfill again (it also runs at startup); idempotent.</summary>
    [HttpPost("backfill")]
    public async Task<ActionResult<MinecraftCatalogBackfillResultDto>> Backfill(CancellationToken ct)
    {
        return Ok(await _maintenance.BackfillAsync(ct));
    }

    /// <summary>Points everything that references <paramref name="sourceId"/> at <paramref name="targetId"/> and deletes the source.</summary>
    [HttpPost("materials/{sourceId:int}/merge-into/{targetId:int}")]
    public async Task<ActionResult<MinecraftMaterialMergeResultDto>> Merge(int sourceId, int targetId, CancellationToken ct)
    {
        try
        {
            return Ok(await _maintenance.MergeAsync(sourceId, targetId, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
