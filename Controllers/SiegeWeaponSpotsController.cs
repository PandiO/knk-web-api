using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege weapons phase 1 (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 4, § 9.3; PandiO/knk-workspace#782):
    // a scenario's weapon spots are owned children like its objectives. Listed and created under the scenario
    // (GET/POST /api/SiegeScenarios/{id}/weapon-spots, served here so SiegeScenariosController stays unchanged),
    // otherwise addressed by their own id.
    [ApiController]
    [Route("api/[controller]")]
    [DashboardReads("SiegeWeaponSpot")]
    public class SiegeWeaponSpotsController : ControllerBase
    {
        private readonly ISiegeWeaponService _service;

        public SiegeWeaponSpotsController(ISiegeWeaponService service)
        {
            _service = service;
        }

        [HttpGet("/api/SiegeScenarios/{siegeScenarioId:int}/weapon-spots")]
        public async Task<IActionResult> GetByScenario(int siegeScenarioId)
        {
            try
            {
                return Ok(await _service.GetSpotsAsync(siegeScenarioId));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [RequireDashboardChange("SiegeWeaponSpot")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPost("/api/SiegeScenarios/{siegeScenarioId:int}/weapon-spots")]
        public async Task<IActionResult> Create(int siegeScenarioId, [FromBody] SiegeWeaponSpotUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateSpotAsync(siegeScenarioId, dto);
                return CreatedAtRoute("GetSiegeWeaponSpotById", new { id = created.Id }, created);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{id:int}", Name = "GetSiegeWeaponSpotById")]
        public async Task<IActionResult> GetById(int id)
        {
            var spot = await _service.GetSpotByIdAsync(id);
            return spot == null ? NotFound() : Ok(spot);
        }

        [RequireDashboardChange("SiegeWeaponSpot")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SiegeWeaponSpotUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateSpotAsync(id, dto);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        // Area node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280).
        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _service.DeleteSpotAsync(id);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
