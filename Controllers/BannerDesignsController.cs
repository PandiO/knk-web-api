using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege Phase 1 (docs/specs/siege-minigame/DESIGN.md §3.1). Layers are an owned child
    // collection: listed/created under their banner here, addressed singly by BannerLayersController.
    [ApiController]
    [Route("api/[controller]")]
    public class BannerDesignsController : ControllerBase
    {
        private readonly IBannerDesignService _service;

        public BannerDesignsController(IBannerDesignService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetBannerDesignById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        // Valid BannerLayer.PatternKey values for the running server version.
        [HttpGet("pattern-keys")]
        public IActionResult GetPatternKeys() => Ok(_service.GetPatternKeys());

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] BannerDesignUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetBannerDesignById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] BannerDesignUpsertDto dto)
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
        public async Task<ActionResult<PagedResultDto<BannerDesignListDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchAsync(query));
        }

        [HttpGet("{id:int}/layers")]
        public async Task<IActionResult> GetLayers(int id)
        {
            try
            {
                return Ok(await _service.GetLayersAsync(id));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost("{id:int}/layers")]
        public async Task<IActionResult> CreateLayer(int id, [FromBody] BannerLayerUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateLayerAsync(id, dto);
                return CreatedAtRoute("GetBannerLayerById", new { id = created.Id }, created);
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
