using knkwebapi_v2.Attributes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Services;
using knkwebapi_v2.Dtos;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.content or the game server; reads stay logged-in.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageContent)]
    public class FormStepsController : ControllerBase
    {
        private readonly IFormStepService _service;

        public FormStepsController(IFormStepService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllReusable()
        {
            var steps = await _service.GetAllReusableAsync();
            return Ok(steps);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var step = await _service.GetByIdAsync(id);
            if (step == null) return NotFound();
            return Ok(step);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] FormStepDto stepDto)
        {
            if (stepDto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(stepDto);
                return CreatedAtAction(nameof(GetById), new { id = int.Parse(created.Id!) }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] FormStepDto stepDto)
        {
            if (stepDto == null) return BadRequest();
            try
            {
                await _service.UpdateAsync(id, stepDto);
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
        }
    }
}
