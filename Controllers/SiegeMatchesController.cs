using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Statistics;

namespace KnKWebAPI.Controllers
{
    // Siege Phase 6 (docs/specs/siege-minigame/DESIGN.md §3.10, §7.6, §11.2): the plugin's match
    // lifecycle checkpoints and the match history. Served at the DESIGN kebab-case path
    // (/api/siege-matches) and at the PascalCase controller path every other controller uses.
    // Writes carry [RequirePluginService] (X-API-Key = Security:PluginApiKey; fails closed, KNG-22).
    // Phase 7a adds the gate lockdown: GET {id}/gate-snapshots, POST {id}/gate-lockdown,
    // POST {id}/gate-restore, POST restore-stale-gates.
    [ApiController]
    [Route("api/siege-matches")]
    [Route("api/[controller]")]
    public class SiegeMatchesController : ControllerBase
    {
        private readonly ISiegeMatchService _service;
        private readonly ISiegeMatchGateService? _gates;
        private readonly ISiegeMatchHistoryService? _history;
        private readonly IStatisticsViewerResolver? _viewers;

        public SiegeMatchesController(ISiegeMatchService service, ISiegeMatchGateService? gates = null,
            ISiegeMatchHistoryService? history = null, IStatisticsViewerResolver? viewers = null)
        {
            _service = service;
            _gates = gates;
            _history = history;
            _viewers = viewers;
        }

        // ===== Alpha run 1 (PandiO/knk-workspace#174): the public siege results log =====
        // The history reads above need the plugin key or a login and show every number. The public profile and the match
        // page are open to anyone (signed out too), so these two return finished matches only, with each participant's
        // own numbers filtered by their statistics visibility settings (SiegeMatchHistoryService).

        /// <summary>A player's finished matches, newest first (limit 1-50, default 20), filtered for the caller.</summary>
        /// <response code="404">Unknown or inactive player</response>
        // Public by design (#174, like the statistics reads): pinned in EndpointAccessRulesTests.
        [AllowAnonymous]
        [HttpGet("public/users/{userId:int}")]
        [ProducesResponseType(typeof(List<SiegeMatchPublicSummaryDto>), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> PublicPlayerMatches(int userId, [FromQuery] int limit = 20)
        {
            if (_history == null || _viewers == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
            var viewer = await _viewers.ResolveAsync(HttpContext.GetKnkCaller(), userId);
            var rows = await _history.GetPlayerMatchesAsync(userId, viewer, limit);
            return rows == null
                ? NotFound(new { error = "UserNotFound", message = $"User with ID {userId} not found" })
                : Ok(rows);
        }

        /// <summary>One finished match for the public match page, filtered for the caller.</summary>
        /// <response code="404">No such finished match (running ones aren't shown)</response>
        // Public by design (#174): pinned in EndpointAccessRulesTests.
        [AllowAnonymous]
        [HttpGet("public/{id:int}")]
        [ProducesResponseType(typeof(SiegeMatchPublicDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> PublicMatch(int id)
        {
            if (_history == null || _viewers == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
            // No target player: "self" is decided per participant (SiegeMatchHistoryService.CanSee).
            var viewer = await _viewers.ResolveAsync(HttpContext.GetKnkCaller(), 0);
            var match = await _history.GetMatchAsync(id, viewer);
            return match == null
                ? NotFound(new { error = "MatchNotFound", message = $"No finished siege match {id}." })
                : Ok(match);
        }

        // History: newest first, optionally filtered (userId also returns that user's own row).
        [HttpGet]
        public async Task<ActionResult<List<SiegeMatchSummaryDto>>> Query(
            [FromQuery] int? userId,
            [FromQuery] int? lobbyId,
            [FromQuery] SiegeMatchStatus? status,
            [FromQuery] int limit = 50)
        {
            return Ok(await _service.QueryAsync(userId, lobbyId, status, limit));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var match = await _service.GetByIdAsync(id);
            return match == null ? NotFound() : Ok(match);
        }

        [HttpPost]
        [RequirePluginService]
        public async Task<IActionResult> Create([FromBody] SiegeMatchCreateDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () =>
            {
                var created = await _service.CreateAsync(dto);
                return Created($"/api/siege-matches/{created.Id}", created);
            });
        }

        [HttpPost("{id:int}/start")]
        [RequirePluginService]
        public async Task<IActionResult> Start(int id, [FromBody] SiegeMatchStartDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () => Ok(await _service.StartAsync(id, dto)));
        }

        [HttpPost("{id:int}/participants/{userId:int}/left")]
        [RequirePluginService]
        public async Task<IActionResult> ParticipantLeft(int id, int userId, [FromBody] SiegeMatchParticipantLeftDto? dto)
        {
            return await Run(async () =>
            {
                await _service.ParticipantLeftAsync(id, userId, dto);
                return NoContent();
            });
        }

        // Grants the rewards once; a repeat call returns the stored result ("alreadyCompleted": true).
        [HttpPost("{id:int}/complete")]
        [RequirePluginService]
        public async Task<IActionResult> Complete(int id, [FromBody] SiegeMatchCompleteDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () => Ok(await _service.CompleteAsync(id, dto)));
        }

        [HttpPost("{id:int}/abort")]
        [RequirePluginService]
        public async Task<IActionResult> Abort(int id, [FromBody] SiegeMatchAbortDto? dto)
        {
            return await Run(async () => Ok(await _service.AbortAsync(id, dto ?? new SiegeMatchAbortDto())));
        }

        // Plugin startup recovery: every Created/InProgress match is aborted (default ServerRestart).
        [HttpPost("abort-unfinished")]
        [RequirePluginService]
        public async Task<IActionResult> AbortUnfinished([FromBody] SiegeMatchAbortUnfinishedDto? dto)
        {
            return await Run(async () => Ok(await _service.AbortUnfinishedAsync(dto ?? new SiegeMatchAbortUnfinishedDto())));
        }

        // ---- Phase 7a: gate lockdown (DESIGN §8.2, §8.4) ----

        [HttpGet("{id:int}/gate-snapshots")]
        public async Task<IActionResult> GateSnapshots(int id)
        {
            return await Run(async () => Ok(await Gates().GetSnapshotsAsync(id)));
        }

        // Snapshot → CurrentSiegeId → overrides for the listed gates (one transaction).
        [HttpPost("{id:int}/gate-lockdown")]
        [RequirePluginService]
        public async Task<IActionResult> GateLockdown(int id, [FromBody] SiegeGateLockdownDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () => Ok(await Gates().LockdownAsync(id, dto)));
        }

        // Re-applies and deletes the match's snapshots; returns them for the runtime restore.
        [HttpPost("{id:int}/gate-restore")]
        [RequirePluginService]
        public async Task<IActionResult> GateRestore(int id)
        {
            return await Run(async () => Ok(await Gates().RestoreAsync(id)));
        }

        // Plugin startup recovery (nothing runs yet): every leftover snapshot is re-applied.
        [HttpPost("restore-stale-gates")]
        [RequirePluginService]
        public async Task<IActionResult> RestoreStaleGates()
        {
            return await Run(async () => Ok(await Gates().RestoreStaleAsync()));
        }

        private ISiegeMatchGateService Gates() =>
            _gates ?? throw new InvalidOperationException("Siege gate service is not registered.");

        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { code = "NotFound", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { code = "ValidationFailed", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "BusinessRuleViolation", message = ex.Message });
            }
        }
    }
}
