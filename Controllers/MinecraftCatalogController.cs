using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// The Minecraft catalogue (KNG-200, docs/specs/minecraft-catalog/IMPLEMENTATION_PLAN.md §5.2a, §8):
/// server scans, the plugin's snapshot, audit, backfill, merge and icon overrides. Staff endpoints need
/// knk.admin.content (reads included); only the snapshot upload is for the game server.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MinecraftCatalogController : ControllerBase
{
    private readonly IMinecraftCatalogMaintenanceService _maintenance;
    private readonly IMinecraftCatalogScanService _scans;

    public MinecraftCatalogController(IMinecraftCatalogMaintenanceService maintenance, IMinecraftCatalogScanService scans)
    {
        _maintenance = maintenance;
        _scans = scans;
    }

    /// <summary>Asks knk-plugin to scan the server's registry (a headless WorldTask).</summary>
    [HttpPost("scans")]
    [RequirePermission(StaffPermissions.ManageContent)]
    public async Task<ActionResult<MinecraftCatalogScanDto>> RequestScan(CancellationToken ct)
    {
        var userId = HttpContext.GetKnkCaller().WebUserId;
        if (userId == null) return Forbid();
        try
        {
            var scan = await _scans.RequestScanAsync(userId.Value, ct);
            return CreatedAtAction(nameof(GetScan), new { id = scan.Id }, scan);
        }
        catch (MinecraftCatalogConflictException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("scans")]
    [RequirePermission(StaffPermissions.ManageContent)]
    public async Task<ActionResult<IReadOnlyList<MinecraftCatalogScanDto>>> ListScans([FromQuery] int take = 20, CancellationToken ct = default)
    {
        return Ok(await _scans.ListAsync(take, ct));
    }

    [HttpGet("scans/{id:int}")]
    [RequirePermission(StaffPermissions.ManageContent)]
    public async Task<ActionResult<MinecraftCatalogScanDto>> GetScan(int id, CancellationToken ct)
    {
        var scan = await _scans.GetAsync(id, ct);
        return scan == null ? NotFound() : Ok(scan);
    }

    [HttpPost("scans/{id:int}/cancel")]
    [RequirePermission(StaffPermissions.ManageContent)]
    public async Task<ActionResult<MinecraftCatalogScanDto>> CancelScan(int id, CancellationToken ct)
    {
        try
        {
            return Ok(await _scans.CancelAsync(id, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (MinecraftCatalogConflictException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>knk-plugin's registry snapshot for a requested scan; validated in full, applied in one transaction.</summary>
    [HttpPut("scans/{id:int}/snapshot")]
    [RequirePluginService]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<ActionResult<MinecraftCatalogScanDto>> PutSnapshot(int id, [FromBody] MinecraftCatalogSnapshotDto snapshot, CancellationToken ct)
    {
        try
        {
            return Ok(await _scans.IngestAsync(id, snapshot, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (MinecraftCatalogConflictException ex)
        {
            return Conflict(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("audit")]
    [RequirePermission(StaffPermissions.ManageContent)]
    public async Task<ActionResult<MinecraftCatalogAuditDto>> Audit(CancellationToken ct)
    {
        return Ok(await _maintenance.AuditAsync(ct));
    }

    /// <summary>Runs the backfill again (it also runs at startup); idempotent.</summary>
    [HttpPost("backfill")]
    [RequirePermission(StaffPermissions.ManageContent)]
    public async Task<ActionResult<MinecraftCatalogBackfillResultDto>> Backfill(CancellationToken ct)
    {
        return Ok(await _maintenance.BackfillAsync(ct));
    }

    /// <summary>Points everything that references <paramref name="sourceId"/> at <paramref name="targetId"/> and deletes the source.</summary>
    [HttpPost("materials/{sourceId:int}/merge-into/{targetId:int}")]
    [RequirePermission(StaffPermissions.ManageContent)]
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

    /// <summary>Sets (or with null clears) the manual icon of a material; a scan never overwrites it.</summary>
    [HttpPut("materials/{id:int}/icon-override")]
    [RequirePermission(StaffPermissions.ManageContent)]
    public async Task<IActionResult> SetIconOverride(int id, [FromBody] MinecraftIconOverrideDto body, CancellationToken ct)
    {
        try
        {
            await _scans.SetIconOverrideAsync(id, body?.IconUrlOverride, ct);
            return NoContent();
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
