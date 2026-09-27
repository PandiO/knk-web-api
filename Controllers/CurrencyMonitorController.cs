using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Currency anomaly alerts and reconciliation (currency-payments DESIGN.md §3.4/§3.9,
    /// IMPLEMENTATION_PLAN.md Phase 5), next to the other staff routes under api/currency/admin.
    /// The alert list and acknowledge are also called by the plugin (/knk currency alerts [ack]),
    /// so they take RequireServiceOrPermission (the plugin checks knk.admin.currency.alerts
    /// in-game first); the reconciliation routes are web only.
    /// </summary>
    [ApiController]
    [Route("api/currency/admin")]
    public class CurrencyMonitorController : ControllerBase
    {
        private readonly ICurrencyAlertService _alerts;

        public CurrencyMonitorController(ICurrencyAlertService alerts)
        {
            _alerts = alerts;
        }

        /// <summary>Alerts, newest first, with the number still open per severity.</summary>
        /// <param name="status">open (default), acked or all</param>
        /// <param name="severity">This severity and above: Low, Medium, High, Critical</param>
        /// <param name="rule">One rule, R1–R9</param>
        /// <param name="userId">Alerts about this player</param>
        /// <param name="page">1-based</param>
        /// <param name="pageSize">1–100, default 25</param>
        /// <response code="200">A page of alerts</response>
        /// <response code="400">Unknown status or severity</response>
        [RequireServiceOrPermission(StaffPermissions.CurrencyAlerts)]
        [HttpGet("alerts")]
        public async Task<IActionResult> GetAlerts([FromQuery] string? status = null, [FromQuery] string? severity = null,
            [FromQuery] string? rule = null, [FromQuery] int? userId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
            CancellationToken ct = default)
        {
            var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "open" : status.Trim().ToLowerInvariant();
            if (normalizedStatus is not ("open" or "acked" or "acknowledged" or "all"))
            {
                return BadRequest(new { error = "InvalidStatus", message = "status must be open, acked or all." });
            }
            CurrencyAlertSeverity? minSeverity = null;
            if (!string.IsNullOrWhiteSpace(severity))
            {
                if (!Enum.TryParse<CurrencyAlertSeverity>(severity.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                {
                    return BadRequest(new { error = "InvalidSeverity", message = "severity must be Low, Medium, High or Critical." });
                }
                minSeverity = parsed;
            }
            return Ok(await _alerts.ListAsync(new CurrencyAlertQuery
            {
                Status = normalizedStatus,
                MinSeverity = minSeverity,
                Rule = string.IsNullOrWhiteSpace(rule) ? null : rule.Trim(),
                UserId = userId is > 0 ? userId : null,
                Page = Math.Max(1, page),
                PageSize = Math.Clamp(pageSize, 1, 100)
            }, ct));
        }

        /// <summary>Marks an alert as handled by the calling staff member (repeatable).</summary>
        /// <response code="200">The alert, acknowledged</response>
        /// <response code="400">The game server didn't name the staff member (X-Acting-User-Id)</response>
        /// <response code="404">No such alert</response>
        [RequireServiceOrPermission(StaffPermissions.CurrencyAlerts)]
        [HttpPost("alerts/{id:long}/ack")]
        public async Task<IActionResult> Acknowledge(long id, CancellationToken ct)
        {
            var actor = HttpContext.GetKnkCaller().ActorUserId;
            if (actor is not > 0)
            {
                return BadRequest(new { error = "ActingUserRequired", message = $"{PluginServiceAuth.ActingUserHeader} must name the staff member." });
            }
            try
            {
                return Ok(await _alerts.AcknowledgeAsync(id, actor.Value, ct));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = "AlertNotFound", message = ex.Message });
            }
        }

        /// <summary>The last reconciliation run since the API started (null until the first one), and whether one is running.</summary>
        [RequirePermission(StaffPermissions.CurrencyHistory)]
        [HttpGet("reconciliation")]
        public IActionResult GetReconciliation() => Ok(_alerts.GetReconciliationStatus());

        /// <summary>
        /// Runs the reconciliation now. Report only: mismatches become R1/R2 alerts (and R1 switches
        /// transfers off); nothing is corrected.
        /// </summary>
        /// <response code="200">The run</response>
        /// <response code="409">A run is already going on</response>
        [RequirePermission(StaffPermissions.CurrencyAlerts)]
        [HttpPost("reconciliation/run")]
        public async Task<IActionResult> RunReconciliation(CancellationToken ct)
        {
            var run = await _alerts.RunReconciliationAsync("manual", HttpContext.GetKnkCaller().ActorUserId, ct);
            return run == null
                ? Conflict(new { error = "ReconciliationRunning", message = "A reconciliation is already running; reload in a moment." })
                : Ok(run);
        }
    }
}
