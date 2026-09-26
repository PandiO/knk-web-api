using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege Phase 6 (docs/specs/siege-minigame/DESIGN.md §3.10, §7.6, §11.2): the plugin's match
    // lifecycle checkpoints and the match history. Served at the DESIGN kebab-case path
    // (/api/siege-matches) and at the PascalCase controller path every other controller uses.
    // Writes carry [RequirePluginServiceKey] (open until Security:PluginServiceKey is set).
    // Phase 7a adds the gate lockdown: GET {id}/gate-snapshots, POST {id}/gate-lockdown,
    // POST {id}/gate-restore, POST restore-stale-gates.
    [ApiController]
    [Route("api/siege-matches")]
    [Route("api/[controller]")]
    public class SiegeMatchesController : ControllerBase
    {
        private readonly ISiegeMatchService _service;
        private readonly ISiegeMatchGateService? _gates;

        public SiegeMatchesController(ISiegeMatchService service, ISiegeMatchGateService? gates = null)
        {
            _service = service;
            _gates = gates;
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
        [RequirePluginServiceKey]
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
        [RequirePluginServiceKey]
        public async Task<IActionResult> Start(int id, [FromBody] SiegeMatchStartDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () => Ok(await _service.StartAsync(id, dto)));
        }

        [HttpPost("{id:int}/participants/{userId:int}/left")]
        [RequirePluginServiceKey]
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
        [RequirePluginServiceKey]
        public async Task<IActionResult> Complete(int id, [FromBody] SiegeMatchCompleteDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () => Ok(await _service.CompleteAsync(id, dto)));
        }

        [HttpPost("{id:int}/abort")]
        [RequirePluginServiceKey]
        public async Task<IActionResult> Abort(int id, [FromBody] SiegeMatchAbortDto? dto)
        {
            return await Run(async () => Ok(await _service.AbortAsync(id, dto ?? new SiegeMatchAbortDto())));
        }

        // Plugin startup recovery: every Created/InProgress match is aborted (default ServerRestart).
        [HttpPost("abort-unfinished")]
        [RequirePluginServiceKey]
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
        [RequirePluginServiceKey]
        public async Task<IActionResult> GateLockdown(int id, [FromBody] SiegeGateLockdownDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () => Ok(await Gates().LockdownAsync(id, dto)));
        }

        // Re-applies and deletes the match's snapshots; returns them for the runtime restore.
        [HttpPost("{id:int}/gate-restore")]
        [RequirePluginServiceKey]
        public async Task<IActionResult> GateRestore(int id)
        {
            return await Run(async () => Ok(await Gates().RestoreAsync(id)));
        }

        // Plugin startup recovery (nothing runs yet): every leftover snapshot is re-applied.
        [HttpPost("restore-stale-gates")]
        [RequirePluginServiceKey]
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
