using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MenuTemplatesController : ControllerBase
    {
        private readonly IMenuTemplateService _service;

        public MenuTemplatesController(IMenuTemplateService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _service.GetAllAsync();
            return Ok(items);
        }

        [HttpGet("{id:int}", Name = "GetMenuTemplateById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpGet("by-key/{key}")]
        public async Task<IActionResult> GetByKey(string key)
        {
            var item = await _service.GetByKeyAsync(key);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] MenuTemplateDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetMenuTemplateById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] MenuTemplateDto dto)
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
            catch (DbUpdateException ex)
            {
                return Conflict(new { code = "DbConstraint", message = ex.Message });
            }
        }
    }
}
