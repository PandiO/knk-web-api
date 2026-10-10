using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Location retention (KNG-80): the orphaned-Location review panel's API, laid out like the
    /// currency alerts (CurrencyMonitorController). Every route has an explicit permission check.
    /// The list and the teleport target are also called by the plugin (/knk location orphans,
    /// /knk location tp), so they take RequireServiceOrPermission and the plugin checks the node
    /// in game first; keep, delete, run and settings are web only.
    /// </summary>
    [ApiController]
    [Route("api/location-retention")]
    public class LocationRetentionController : ControllerBase
    {
        private readonly ILocationRetentionService _retention;

        public LocationRetentionController(ILocationRetentionService retention)
        {
            _retention = retention;
        }

        /// <summary>Review items, newest first, with the open/kept totals.</summary>
        /// <param name="status">open (default), kept, deleted, resolved or all</param>
        /// <param name="page">1-based</param>
        /// <param name="pageSize">1–100, default 25</param>
        /// <response code="400">Unknown status</response>
        [RequireServiceOrPermission(StaffPermissions.LocationOrphansView)]
        [HttpGet("orphans")]
        public async Task<IActionResult> GetOrphans([FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
            CancellationToken ct = default)
        {
            var normalized = string.IsNullOrWhiteSpace(status) ? "open" : status.Trim().ToLowerInvariant();
            if (normalized is not ("open" or "kept" or "deleted" or "resolved" or "all"))
            {
                return BadRequest(new { error = "InvalidStatus", message = "status must be open, kept, deleted, resolved or all." });
            }
            return Ok(await _retention.ListAsync(normalized, page, pageSize, ct));
        }

        /// <summary>Marks an open item Kept (with an optional note).</summary>
        /// <response code="404">No such item</response>
        /// <response code="409">The item isn't open</response>
        [RequirePermission(StaffPermissions.LocationOrphansKeep)]
        [HttpPost("orphans/{id:int}/keep")]
        public async Task<IActionResult> Keep(int id, [FromBody] LocationOrphanDecisionDto? body, CancellationToken ct)
        {
            var actor = HttpContext.GetKnkCaller().ActorUserId;
            if (actor is not > 0) return ActingUserRequired();
            try
            {
                return Ok(await _retention.KeepAsync(id, actor.Value, body?.Note, ct));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = "OrphanNotFound", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = "OrphanNotOpen", message = ex.Message });
            }
        }

        /// <summary>
        /// Deletes the item's Location after re-checking, in the same transaction, that it is still an
        /// orphan. 200 with outcome Deleted, or 409 with outcome NoLongerOrphan (nothing deleted, item Resolved).
        /// </summary>
        /// <response code="404">No such item</response>
        /// <response code="409">The item isn't open, or the Location is no longer an orphan</response>
        [RequirePermission(StaffPermissions.LocationOrphansDelete)]
        [HttpPost("orphans/{id:int}/delete")]
        public async Task<IActionResult> Delete(int id, [FromBody] LocationOrphanDecisionDto? body, CancellationToken ct)
        {
            var actor = HttpContext.GetKnkCaller().ActorUserId;
            if (actor is not > 0) return ActingUserRequired();
            try
            {
                var result = await _retention.DeleteAsync(id, actor.Value, body?.Note, ct);
                return result.Outcome == "Deleted" ? Ok(result) : Conflict(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = "OrphanNotFound", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = "OrphanNotOpen", message = ex.Message });
            }
        }

        /// <summary>Settings, the last run, the next scheduled run and what counts as a reference.</summary>
        [RequirePermission(StaffPermissions.LocationOrphansView)]
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus(CancellationToken ct) => Ok(await _retention.GetStatusAsync(ct));

        /// <summary>"Run check now". Flags only; never deletes.</summary>
        /// <response code="409">A run is already going on</response>
        [RequirePermission(StaffPermissions.LocationOrphansRun)]
        [HttpPost("run")]
        public async Task<IActionResult> Run(CancellationToken ct)
        {
            var run = await _retention.RunCheckAsync("manual", HttpContext.GetKnkCaller().ActorUserId, null, ct);
            return run == null
                ? Conflict(new { error = "RunInProgress", message = "A check is already running; reload in a moment." })
                : Ok(run);
        }

        /// <summary>Changes the schedule, grace period and Keep recheck period.</summary>
        /// <response code="400">An invalid value</response>
        [RequirePermission(StaffPermissions.LocationRetentionSettings)]
        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] LocationRetentionSettingsDto settings, CancellationToken ct)
        {
            try
            {
                return Ok(await _retention.UpdateSettingsAsync(settings, HttpContext.GetKnkCaller().ActorUserId, ct));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "InvalidSettings", message = ex.Message });
            }
        }

        /// <summary>Any Location's world and coordinates, for /knk location tp.</summary>
        /// <response code="404">No such Location</response>
        [RequireServiceOrPermission(StaffPermissions.LocationTeleport)]
        [HttpGet("locations/{locationId:int}/teleport-target")]
        public async Task<IActionResult> GetTeleportTarget(int locationId, CancellationToken ct)
        {
            var target = await _retention.GetTeleportTargetAsync(locationId, ct);
            return target == null
                ? NotFound(new { error = "LocationNotFound", message = $"Location {locationId} not found." })
                : Ok(target);
        }

        private IActionResult ActingUserRequired() =>
            BadRequest(new { error = "ActingUserRequired", message = "The staff member making the decision must be known." });
    }
}
