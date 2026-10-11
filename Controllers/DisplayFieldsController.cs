using knkwebapi_v2.Attributes;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.content or the game server; reads stay logged-in.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageContent)]
    public class DisplayFieldsController : ControllerBase
    {
        private readonly IDisplayFieldService _service;
        private readonly ILogger<DisplayFieldsController> _logger;

        public DisplayFieldsController(
            IDisplayFieldService service,
            ILogger<DisplayFieldsController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// Get all reusable field templates.
        /// </summary>
        [HttpGet("reusable")]
        public async Task<ActionResult<IEnumerable<DisplayFieldDto>>> GetAllReusableAsync()
        {
            try
            {
                var fields = await _service.GetAllReusableAsync();
                return Ok(fields);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving reusable fields");
                return StatusCode(500, new { message = "An error occurred while retrieving fields." });
            }
        }

        /// <summary>
        /// Get field by ID.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<DisplayFieldDto>> GetByIdAsync(int id)
        {
            try
            {
                var field = await _service.GetByIdAsync(id);
                if (field == null)
                    return NotFound(new { message = $"DisplayField with id {id} not found." });

                return Ok(field);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving field {Id}", id);
                return StatusCode(500, new { message = "An error occurred while retrieving the field." });
            }
        }

        /// <summary>
        /// Create new reusable field template.
        /// </summary>
        [HttpPost("reusable")]
        public async Task<ActionResult<DisplayFieldDto>> CreateReusableAsync(
            [FromBody] DisplayFieldDto field)
        {
            try
            {
                var created = await _service.CreateReusableAsync(field);
                return CreatedAtAction(
                    nameof(GetByIdAsync), 
                    new { id = int.Parse(created.Id ?? "0") }, 
                    created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating reusable field");
                return StatusCode(500, new { message = "An error occurred while creating the field." });
            }
        }

        /// <summary>
        /// Update field.
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateAsync(int id, [FromBody] DisplayFieldDto field)
        {
            try
            {
                await _service.UpdateAsync(id, field);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating field {Id}", id);
                return StatusCode(500, new { message = "An error occurred while updating the field." });
            }
        }

        /// <summary>
        /// Delete field.
        /// </summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteAsync(int id)
        {
            try
            {
                await _service.DeleteAsync(id);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting field {Id}", id);
                return StatusCode(500, new { message = "An error occurred while deleting the field." });
            }
        }

        /// <summary>
        /// Clone field (Copy or Link mode).
        /// </summary>
        [HttpPost("{id:int}/clone")]
        public async Task<ActionResult<DisplayFieldDto>> CloneFieldAsync(
            int id,
            [FromBody] CloneFieldRequest request)
        {
            try
            {
                var cloned = await _service.CloneFieldAsync(id, request.LinkMode);
                return Ok(cloned);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cloning field {Id}", id);
                return StatusCode(500, new { message = "An error occurred while cloning the field." });
            }
        }

        public class CloneFieldRequest
        {
            public ReuseLinkMode LinkMode { get; set; }
        }
    }
}
