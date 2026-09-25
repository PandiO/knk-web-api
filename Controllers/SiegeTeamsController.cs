using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Single-team operations (docs/specs/siege-minigame/DESIGN.md §3.4). Listing and creation live
    // under the parent scenario (GET/POST /api/SiegeScenarios/{id}/teams); spawnpoints are the team's
    // own owned children, listed/created here and addressed singly by SiegeSpawnpointsController.
    [ApiController]
    [Route("api/[controller]")]
    public class SiegeTeamsController : ControllerBase
    {
        private readonly ISiegeScenarioService _service;

        public SiegeTeamsController(ISiegeScenarioService service)
        {
            _service = service;
        }

        [HttpGet("{id:int}", Name = "GetSiegeTeamById")]
        public async Task<IActionResult> GetById(int id)
        {
            var team = await _service.GetTeamByIdAsync(id);
            return team == null ? NotFound() : Ok(team);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SiegeTeamUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateTeamAsync(id, dto);
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
                await _service.DeleteTeamAsync(id);
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

        // Picker source for InitialHolderTeamId / InitialOwnerTeamId - pass
        // filters: { "siegeScenarioId": "<id>" } to scope it to one scenario.
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<SiegeTeamReadDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchTeamsAsync(query));
        }

        [HttpGet("{id:int}/spawnpoints")]
        public async Task<IActionResult> GetSpawnpoints(int id)
        {
            try
            {
                return Ok(await _service.GetSpawnpointsAsync(id));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost("{id:int}/spawnpoints")]
        public async Task<IActionResult> CreateSpawnpoint(int id, [FromBody] SiegeSpawnpointUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateSpawnpointAsync(id, dto);
                return CreatedAtRoute("GetSiegeSpawnpointById", new { id = created.Id }, created);
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
