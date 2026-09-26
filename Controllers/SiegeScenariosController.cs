using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege Phase 2 (docs/specs/siege-minigame/DESIGN.md §3.3–3.9, §11.2). Teams and objectives are
    // owned child collections: listed/created under their scenario here, addressed singly by
    // SiegeTeamsController / SiegeObjectivesController (GateStructures/{id}/doors pattern). The
    // scenario's own create/update ignores them.
    [ApiController]
    [Route("api/[controller]")]
    public class SiegeScenariosController : ControllerBase
    {
        private readonly ISiegeScenarioService _service;

        public SiegeScenariosController(ISiegeScenarioService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetSiegeScenarioById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SiegeScenarioUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetSiegeScenarioById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SiegeScenarioUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateAsync(id, dto);
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
                await _service.DeleteAsync(id);
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
            catch (DbUpdateException ex)
            {
                return Conflict(new { code = "DbConstraint", message = ex.Message });
            }
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<SiegeScenarioListDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchAsync(query));
        }

        // DESIGN §3.9 readiness: errors block lobby rotation, warnings don't. Served at the DESIGN
        // §11.2 kebab-case path and at this controller's own path.
        [HttpGet("{id:int}/readiness")]
        [HttpGet("/api/siege-scenarios/{id:int}/readiness")]
        public async Task<IActionResult> GetReadiness(int id)
        {
            try
            {
                return Ok(await _service.GetReadinessAsync(id));
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

        // ---- Owned teams ----

        [HttpGet("{id:int}/teams")]
        public async Task<IActionResult> GetTeams(int id)
        {
            try
            {
                return Ok(await _service.GetTeamsAsync(id));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost("{id:int}/teams")]
        public async Task<IActionResult> CreateTeam(int id, [FromBody] SiegeTeamUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateTeamAsync(id, dto);
                return CreatedAtRoute("GetSiegeTeamById", new { id = created.Id }, created);
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

        // ---- Owned objectives ----

        [HttpGet("{id:int}/objectives")]
        public async Task<IActionResult> GetObjectives(int id)
        {
            try
            {
                return Ok(await _service.GetObjectivesAsync(id));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost("{id:int}/objectives")]
        public async Task<IActionResult> CreateObjective(int id, [FromBody] SiegeObjectiveUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateObjectiveAsync(id, dto);
                return CreatedAtRoute("GetSiegeObjectiveById", new { id = created.Id }, created);
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
