using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Single-spawnpoint operations (docs/specs/siege-minigame/DESIGN.md §3.5). Listing and creation
    // live under the parent team (GET/POST /api/SiegeTeams/{id}/spawnpoints).
    [ApiController]
    [Route("api/[controller]")]
    public class SiegeSpawnpointsController : ControllerBase
    {
        private readonly ISiegeScenarioService _service;

        public SiegeSpawnpointsController(ISiegeScenarioService service)
        {
            _service = service;
        }

        [HttpGet("{id:int}", Name = "GetSiegeSpawnpointById")]
        public async Task<IActionResult> GetById(int id)
        {
            var spawnpoint = await _service.GetSpawnpointByIdAsync(id);
            return spawnpoint == null ? NotFound() : Ok(spawnpoint);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SiegeSpawnpointUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateSpawnpointAsync(id, dto);
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
                await _service.DeleteSpawnpointAsync(id);
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
