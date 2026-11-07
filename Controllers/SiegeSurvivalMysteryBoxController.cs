using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Survival;

namespace KnKWebAPI.Controllers
{
    // Siege Survival phase 4 (knk-workspace docs/specs/siege-survival/DESIGN.md §8a, S34, S39, S59;
    // PandiO/knk-workspace#41, link S7a): the match-scoped Mystery Box roll, claim and forfeit the plugin calls
    // (link S7b), and the match's roll list (S8's result, debugging). The game server (X-API-Key) or a web user
    // holding the Siege setup node, like the Siege match and survival settings writes; the roll list too, since
    // it is match-internal. Match currency is not here (DESIGN §8): the plugin charges before it rolls.
    [ApiController]
    [Route("api/siege-survival/matches/{siegeMatchId:int}/mystery-box")]
    [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
    public class SiegeSurvivalMysteryBoxController : ControllerBase
    {
        private readonly ISurvivalMysteryBoxRollService _rolls;

        public SiegeSurvivalMysteryBoxController(ISurvivalMysteryBoxRollService rolls)
        {
            _rolls = rolls;
        }

        /// <summary>
        /// Rolls the box point once for the plugin's rollId: 201 with the stored roll; the same rollId with the same
        /// body answers 200 with the stored roll (no second roll); a different body is 409 RollIdReused.
        /// </summary>
        /// <response code="404">Unknown match, point not in the match's scenario, or user not a participant</response>
        /// <response code="409">Match not in progress, participant left, pool invalid, roll id reused</response>
        [HttpPost("{pointId:int}/roll")]
        [ProducesResponseType(typeof(SurvivalMysteryBoxRollDto), 201)]
        [ProducesResponseType(typeof(SurvivalMysteryBoxRollDto), 200)]
        public async Task<IActionResult> Roll(int siegeMatchId, int pointId, [FromBody] SurvivalMysteryBoxRollRequestDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () =>
            {
                var (roll, created) = await _rolls.RollAsync(siegeMatchId, pointId, dto);
                return created
                    ? Created($"/api/siege-survival/matches/{siegeMatchId}/mystery-box/rolls/{Uri.EscapeDataString(roll.RollId)}", roll)
                    : Ok(roll);
            });
        }

        /// <summary>Records the roll's one delivery; a repeat answers the stored state; 409 RollForfeited after a forfeit.</summary>
        [HttpPost("rolls/{rollId}/claim")]
        [ProducesResponseType(typeof(SurvivalMysteryBoxRollDto), 200)]
        public async Task<IActionResult> Claim(int siegeMatchId, string rollId, [FromBody] SurvivalMysteryBoxClaimRequestDto? dto)
        {
            return await Run(async () => Ok(await _rolls.ClaimAsync(siegeMatchId, rollId, dto ?? new SurvivalMysteryBoxClaimRequestDto())));
        }

        /// <summary>Forfeits the roll (idempotent); 409 RollClaimed after a claim.</summary>
        [HttpPost("rolls/{rollId}/forfeit")]
        [ProducesResponseType(typeof(SurvivalMysteryBoxRollDto), 200)]
        public async Task<IActionResult> Forfeit(int siegeMatchId, string rollId, [FromBody] SurvivalMysteryBoxForfeitRequestDto? dto)
        {
            return await Run(async () => Ok(await _rolls.ForfeitAsync(siegeMatchId, rollId, dto ?? new SurvivalMysteryBoxForfeitRequestDto())));
        }

        /// <summary>The match's rolls, oldest first.</summary>
        [HttpGet("rolls")]
        [ProducesResponseType(typeof(List<SurvivalMysteryBoxRollDto>), 200)]
        public async Task<IActionResult> List(int siegeMatchId)
        {
            return await Run(async () => Ok(await _rolls.ListAsync(siegeMatchId)));
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
