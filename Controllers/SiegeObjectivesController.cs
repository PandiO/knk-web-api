using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Single-objective operations (docs/specs/siege-minigame/DESIGN.md §3.6). Listing and creation
    // live under the parent scenario (GET/POST /api/SiegeScenarios/{id}/objectives).
    [ApiController]
    [Route("api/[controller]")]
    public class SiegeObjectivesController : ControllerBase
    {
        private readonly ISiegeScenarioService _service;

        public SiegeObjectivesController(ISiegeScenarioService service)
        {
            _service = service;
        }

        [HttpGet("{id:int}", Name = "GetSiegeObjectiveById")]
        public async Task<IActionResult> GetById(int id)
        {
            var objective = await _service.GetObjectiveByIdAsync(id);
            return objective == null ? NotFound() : Ok(objective);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SiegeObjectiveUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateObjectiveAsync(id, dto);
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

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _service.DeleteObjectiveAsync(id);
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
