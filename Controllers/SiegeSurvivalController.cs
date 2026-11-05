using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege Survival phase 1 (knk-workspace docs/specs/siege-survival/DESIGN.md §10; PandiO/knk-workspace#41, link
    // S4): the survival settings of a scenario (1:1) and of a lobby (1:1), the frozen match configuration and
    // readiness the plugin reads with the service key, and the editor's per-round preview. The survival child
    // collections (difficulty profiles, monster types, special waves, spawnzones, target overrides, purchase
    // points, Mystery Box pools/entries/points) have their own controllers (SurvivalChildController).
    // Writes need the Siege setup node, as the Siege scenario endpoints do; reads are "logged in or the game server".
    [ApiController]
    [Route("api/[controller]")]
    public class SiegeSurvivalController : ControllerBase
    {
        private readonly ISiegeSurvivalService _service;

        public SiegeSurvivalController(ISiegeSurvivalService service)
        {
            _service = service;
        }

        /// <summary>Every scenario that has survival settings.</summary>
        [HttpGet("scenarios")]
        [HttpGet("/api/siege-survival/scenarios")]
        public async Task<IActionResult> GetScenarios() => Ok(await _service.GetScenarioSummariesAsync());

        // ---- Scenario settings ----

        [HttpGet("scenarios/{siegeScenarioId:int}/settings", Name = "GetSiegeSurvivalSettings")]
        public async Task<IActionResult> GetSettings(int siegeScenarioId)
        {
            var settings = await _service.GetSettingsAsync(siegeScenarioId);
            return settings == null ? NotFound() : Ok(settings);
        }

        /// <summary>Creates or replaces the scenario's survival settings.</summary>
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPut("scenarios/{siegeScenarioId:int}/settings")]
        public async Task<IActionResult> UpsertSettings(int siegeScenarioId, [FromBody] SiegeSurvivalSettingsUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () => Ok(await _service.UpsertSettingsAsync(siegeScenarioId, dto)));
        }

        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpDelete("scenarios/{siegeScenarioId:int}/settings")]
        public async Task<IActionResult> DeleteSettings(int siegeScenarioId)
        {
            return await Run(async () =>
            {
                await _service.DeleteSettingsAsync(siegeScenarioId);
                return NoContent();
            });
        }

        // ---- Frozen configuration, readiness, preview ----

        /// <summary>
        /// The complete frozen survival configuration of a scenario for one match, every default resolved;
        /// lobbyId resolves the lobby's all-captured override, difficultyProfileId picks a profile (default: the
        /// scenario's default profile). 404 for a scenario without survival settings (an ordinary PvP scenario).
        /// </summary>
        [HttpGet("scenarios/{siegeScenarioId:int}/configuration")]
        [HttpGet("/api/siege-survival/scenarios/{siegeScenarioId:int}/configuration")]
        public async Task<IActionResult> GetConfiguration(int siegeScenarioId, [FromQuery] int? lobbyId, [FromQuery] int? difficultyProfileId)
        {
            return await Run(async () => Ok(await _service.GetConfigurationAsync(siegeScenarioId, lobbyId, difficultyProfileId)));
        }

        /// <summary>Survival readiness: the Siege structural rules plus the survival rules; errors block the match start.</summary>
        [HttpGet("scenarios/{siegeScenarioId:int}/readiness")]
        [HttpGet("/api/siege-survival/scenarios/{siegeScenarioId:int}/readiness")]
        public async Task<IActionResult> GetReadiness(int siegeScenarioId, [FromQuery] int? lobbyId, [FromQuery] int? difficultyProfileId)
        {
            return await Run(async () => Ok(await _service.GetReadinessAsync(siegeScenarioId, lobbyId, difficultyProfileId)));
        }

        /// <summary>The enemy count and stat multipliers per round for a difficulty profile (DESIGN §11).</summary>
        [HttpGet("scenarios/{siegeScenarioId:int}/preview")]
        public async Task<IActionResult> GetPreview(int siegeScenarioId, [FromQuery] int? difficultyProfileId,
            [FromQuery] int fromRound = 1, [FromQuery] int toRound = 30, [FromQuery] int players = 1)
        {
            return await Run(async () => Ok(await _service.PreviewAsync(siegeScenarioId, difficultyProfileId, fromRound, toRound, players)));
        }

        // ---- Lobby settings ----

        [HttpGet("lobbies/{siegeLobbyId:int}/settings", Name = "GetSurvivalLobbySettings")]
        public async Task<IActionResult> GetLobbySettings(int siegeLobbyId)
        {
            var settings = await _service.GetLobbySettingsAsync(siegeLobbyId);
            return settings == null ? NotFound() : Ok(settings);
        }

        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPut("lobbies/{siegeLobbyId:int}/settings")]
        public async Task<IActionResult> UpsertLobbySettings(int siegeLobbyId, [FromBody] SurvivalLobbySettingsUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () => Ok(await _service.UpsertLobbySettingsAsync(siegeLobbyId, dto)));
        }

        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpDelete("lobbies/{siegeLobbyId:int}/settings")]
        public async Task<IActionResult> DeleteLobbySettings(int siegeLobbyId)
        {
            return await Run(async () =>
            {
                await _service.DeleteLobbySettingsAsync(siegeLobbyId);
                return NoContent();
            });
        }

        private static async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (KeyNotFoundException ex)
            {
                return new NotFoundObjectResult(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return new BadRequestObjectResult(ex.Message);
            }
            catch (InvalidOperationException ex) when (!DeleteConstraintExceptionFilter.IsConstraintFailure(ex))
            {
                return new ConflictObjectResult(new { code = "BusinessRuleViolation", message = ex.Message });
            }
        }
    }
}
