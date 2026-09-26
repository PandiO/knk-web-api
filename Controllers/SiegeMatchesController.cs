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
    // GET {id}/gate-snapshots is Phase 7.
    [ApiController]
    [Route("api/siege-matches")]
    [Route("api/[controller]")]
    public class SiegeMatchesController : ControllerBase
    {
        private readonly ISiegeMatchService _service;

        public SiegeMatchesController(ISiegeMatchService service)
        {
            _service = service;
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
