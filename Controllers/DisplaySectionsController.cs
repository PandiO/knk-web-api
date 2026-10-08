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
    public class DisplaySectionsController : ControllerBase
    {
        private readonly IDisplaySectionService _service;
        private readonly ILogger<DisplaySectionsController> _logger;

        public DisplaySectionsController(
            IDisplaySectionService service,
            ILogger<DisplaySectionsController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// Get all reusable section templates.
        /// </summary>
        [HttpGet("reusable")]
        public async Task<ActionResult<IEnumerable<DisplaySectionDto>>> GetAllReusableAsync()
        {
            try
            {
                var sections = await _service.GetAllReusableAsync();
                return Ok(sections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving reusable sections");
                return StatusCode(500, new { message = "An error occurred while retrieving sections." });
            }
        }

        /// <summary>
        /// Get section by ID.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<DisplaySectionDto>> GetByIdAsync(int id)
        {
            try
            {
                var section = await _service.GetByIdAsync(id);
                if (section == null)
                    return NotFound(new { message = $"DisplaySection with id {id} not found." });

                return Ok(section);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving section {Id}", id);
                return StatusCode(500, new { message = "An error occurred while retrieving the section." });
            }
        }

        /// <summary>
        /// Create new reusable section template.
        /// </summary>
        [HttpPost("reusable")]
        public async Task<ActionResult<DisplaySectionDto>> CreateReusableAsync(
            [FromBody] DisplaySectionDto section)
        {
            try
            {
                var created = await _service.CreateReusableAsync(section);
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
                _logger.LogError(ex, "Error creating reusable section");
                return StatusCode(500, new { message = "An error occurred while creating the section." });
            }
        }

        /// <summary>
        /// Update section.
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateAsync(int id, [FromBody] DisplaySectionDto section)
        {
            try
            {
                await _service.UpdateAsync(id, section);
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
                _logger.LogError(ex, "Error updating section {Id}", id);
                return StatusCode(500, new { message = "An error occurred while updating the section." });
            }
        }

        /// <summary>
        /// Delete section.
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
                _logger.LogError(ex, "Error deleting section {Id}", id);
                return StatusCode(500, new { message = "An error occurred while deleting the section." });
            }
        }

        /// <summary>
        /// Clone section (Copy or Link mode).
        /// </summary>
        [HttpPost("{id:int}/clone")]
        public async Task<ActionResult<DisplaySectionDto>> CloneSectionAsync(
            int id,
            [FromBody] CloneSectionRequest request)
        {
            try
            {
                var cloned = await _service.CloneSectionAsync(id, request.LinkMode);
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
                _logger.LogError(ex, "Error cloning section {Id}", id);
                return StatusCode(500, new { message = "An error occurred while cloning the section." });
            }
        }

        public class CloneSectionRequest
        {
            public ReuseLinkMode LinkMode { get; set; }
        }
    }
}
