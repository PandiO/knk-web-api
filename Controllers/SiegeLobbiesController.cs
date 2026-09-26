using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege Phase 2 (docs/specs/siege-minigame/DESIGN.md §3.8, D1, §11.2): lobby CRUD (rotation is an
    // M2M join in the lobby payload) plus the plugin's runtime-config.
    [ApiController]
    [Route("api/[controller]")]
    public class SiegeLobbiesController : ControllerBase
    {
        private readonly ISiegeLobbyService _service;

        public SiegeLobbiesController(ISiegeLobbyService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetSiegeLobbyById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        // Enabled lobbies + rotations + fully-resolved READY scenarios + the global configuration, in
        // one payload for the plugin's cache. Served at the DESIGN §11.2 kebab-case path and at this
        // controller's own path.
        [HttpGet("runtime-config")]
        [HttpGet("/api/siege-lobbies/runtime-config")]
        public async Task<ActionResult<SiegeRuntimeConfigDto>> GetRuntimeConfig()
        {
            return Ok(await _service.GetRuntimeConfigAsync());
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SiegeLobbyUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetSiegeLobbyById", new { id = created.Id }, created);
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

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SiegeLobbyUpsertDto dto)
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
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "BusinessRuleViolation", message = ex.Message });
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
        public async Task<ActionResult<PagedResultDto<SiegeLobbyListDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchAsync(query));
        }
    }
}
