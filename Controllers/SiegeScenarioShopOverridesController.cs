using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege shop (knk-workspace docs/specs/siege-shop/DESIGN.md § 5.3, § 6.1.3, § 6.2.1; PandiO/knk-workspace#867):
    // a scenario's shop overrides are owned children like its weapon spots. Listed and created under the scenario
    // (GET/POST /api/SiegeScenarios/{id}/shop-overrides, served here so SiegeScenariosController stays unchanged),
    // otherwise addressed by their own id. One override per (scenario, entry): a second is a 409.
    [ApiController]
    [Route("api/[controller]")]
    [DashboardReads("SiegeScenarioShopOverride")]
    public class SiegeScenarioShopOverridesController : ControllerBase
    {
        private readonly ISiegeShopService _service;

        public SiegeScenarioShopOverridesController(ISiegeShopService service)
        {
            _service = service;
        }

        [HttpGet("/api/SiegeScenarios/{siegeScenarioId:int}/shop-overrides")]
        public async Task<IActionResult> GetByScenario(int siegeScenarioId)
        {
            try
            {
                return Ok(await _service.GetOverridesAsync(siegeScenarioId));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [RequireDashboardChange("SiegeScenarioShopOverride")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPost("/api/SiegeScenarios/{siegeScenarioId:int}/shop-overrides")]
        public async Task<IActionResult> Create(int siegeScenarioId, [FromBody] SiegeScenarioShopOverrideUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateOverrideAsync(siegeScenarioId, dto);
                return CreatedAtRoute("GetSiegeScenarioShopOverrideById", new { id = created.Id }, created);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "BusinessRuleViolation", message = ex.Message });
            }
        }

        [HttpGet("{id:int}", Name = "GetSiegeScenarioShopOverrideById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetOverrideByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [RequireDashboardChange("SiegeScenarioShopOverride")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SiegeScenarioShopOverrideUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateOverrideAsync(id, dto);
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
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "BusinessRuleViolation", message = ex.Message });
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
                await _service.DeleteOverrideAsync(id);
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
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "BusinessRuleViolation", message = ex.Message });
            }
        }
    }
}
