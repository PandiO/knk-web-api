using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Survival;

namespace KnKWebAPI.Controllers
{
    // Siege Survival phase 5 (knk-workspace docs/specs/siege-survival/DESIGN.md §9, §10, §11; PandiO/knk-workspace#41,
    // link S8a): the durable survival match result the plugin posts once at match end (settles the v1 per-kill
    // rewards through the ledger), the match's latest checkpoint (telemetry / recovery aid, S61) and the results
    // history list (S9's history page). Writes and the checkpoint read need the game server (X-API-Key) or a web user
    // with the Siege setup node, like the Mystery Box endpoints (S7a); the result read and the list are "logged in or
    // the game server" like the S4 reads, so players can see their own history.
    [ApiController]
    [Route("api/siege-survival")]
    public class SiegeSurvivalMatchResultsController : ControllerBase
    {
        private readonly ISurvivalMatchResultService _results;

        public SiegeSurvivalMatchResultsController(ISurvivalMatchResultService results)
        {
            _results = results;
        }

        /// <summary>
        /// Stores the match's result once and settles: 201 with the stored result; the same body again answers 200
        /// with the stored result (nothing settles twice); a different body is 409 ResultAlreadyStored.
        /// </summary>
        /// <response code="404">Unknown match</response>
        /// <response code="409">ResultAlreadyStored, SettlementMismatch, MatchNotInProgress, MatchAborted</response>
        [HttpPost("matches/{siegeMatchId:int}/result")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [ProducesResponseType(typeof(SurvivalMatchResultDto), 201)]
        [ProducesResponseType(typeof(SurvivalMatchResultDto), 200)]
        public async Task<IActionResult> PostResult(int siegeMatchId, [FromBody] SurvivalMatchResultDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () =>
            {
                var (result, created) = await _results.PostResultAsync(siegeMatchId, dto);
                return created ? Created($"/api/siege-survival/matches/{siegeMatchId}/result", result) : Ok(result);
            });
        }

        /// <summary>The match's stored result, or 404 while none is stored.</summary>
        [HttpGet("matches/{siegeMatchId:int}/result")]
        [ProducesResponseType(typeof(SurvivalMatchResultDto), 200)]
        public async Task<IActionResult> GetResult(int siegeMatchId)
        {
            var result = await _results.GetResultAsync(siegeMatchId);
            return result == null ? NotFound(new { code = "NotFound", message = $"Siege match {siegeMatchId} has no survival result." }) : Ok(result);
        }

        /// <summary>Stored results newest first, optionally of one scenario and/or with one user among the players.</summary>
        [HttpGet("results")]
        [ProducesResponseType(typeof(PagedResultDto<SurvivalMatchResultDto>), 200)]
        public async Task<IActionResult> QueryResults([FromQuery] int? scenarioId, [FromQuery] int? userId,
            [FromQuery] int page = 1, [FromQuery] int pageSize = SurvivalMatchResultService.DefaultPageSize)
        {
            return Ok(await _results.QueryResultsAsync(scenarioId, userId, page, pageSize));
        }

        /// <summary>Upserts the match's latest checkpoint: 201 for the first, 200 afterwards (an older capturedAt is ignored).</summary>
        [HttpPut("matches/{siegeMatchId:int}/checkpoint")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [ProducesResponseType(typeof(SurvivalMatchCheckpointDto), 201)]
        [ProducesResponseType(typeof(SurvivalMatchCheckpointDto), 200)]
        public async Task<IActionResult> PutCheckpoint(int siegeMatchId, [FromBody] SurvivalMatchCheckpointDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () =>
            {
                var (checkpoint, created) = await _results.PutCheckpointAsync(siegeMatchId, dto);
                return created ? Created($"/api/siege-survival/matches/{siegeMatchId}/checkpoint", checkpoint) : Ok(checkpoint);
            });
        }

        /// <summary>The match's latest checkpoint, or 404 while none was put.</summary>
        [HttpGet("matches/{siegeMatchId:int}/checkpoint")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [ProducesResponseType(typeof(SurvivalMatchCheckpointDto), 200)]
        public async Task<IActionResult> GetCheckpoint(int siegeMatchId)
        {
            var checkpoint = await _results.GetCheckpointAsync(siegeMatchId);
            return checkpoint == null ? NotFound(new { code = "NotFound", message = $"Siege match {siegeMatchId} has no checkpoint." }) : Ok(checkpoint);
        }

        private static async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (KeyNotFoundException ex)
            {
                return new NotFoundObjectResult(new { code = "NotFound", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return new BadRequestObjectResult(new { code = "ValidationFailed", message = ex.Message });
            }
            catch (SurvivalMatchResultConflictException ex)
            {
                return new ConflictObjectResult(new { code = ex.Code, message = ex.Message });
            }
            catch (SurvivalMysteryBoxConflictException ex)
            {
                return new ConflictObjectResult(new { code = ex.Code, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return new ConflictObjectResult(new { code = "BusinessRuleViolation", message = ex.Message });
            }
        }
    }
}
