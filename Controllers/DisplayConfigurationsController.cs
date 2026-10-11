using knkwebapi_v2.Attributes;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.content or the game server; reads stay logged-in.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageContent)]
    public class DisplayConfigurationsController : ControllerBase
    {
        private readonly IDisplayConfigurationService _service;
        private readonly ILogger<DisplayConfigurationsController> _logger;

        public DisplayConfigurationsController(
            IDisplayConfigurationService service,
            ILogger<DisplayConfigurationsController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// Get all display configurations.
        /// </summary>
        /// <param name="includeDrafts">Include draft configurations (default: true)</param>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<DisplayConfigurationDto>>> GetAllAsync(
            [FromQuery] bool includeDrafts = true)
        {
            try
            {
                var configs = await _service.GetAllAsync(includeDrafts);
                return Ok(configs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving display configurations");
                return StatusCode(500, new { message = "An error occurred while retrieving configurations." });
            }
        }

        /// <summary>
        /// Get display configuration by ID.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<DisplayConfigurationDto>> GetByIdAsync(int id)
        {
            try
            {
                var config = await _service.GetByIdAsync(id);
                if (config == null)
                    return NotFound(new { message = $"DisplayConfiguration with id {id} not found." });

                return Ok(config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving display configuration {Id}", id);
                return StatusCode(500, new { message = "An error occurred while retrieving the configuration." });
            }
        }

        /// <summary>
        /// Get default display configuration for entity type.
        /// </summary>
        [HttpGet("entity/{entityName}")]
        public async Task<ActionResult<DisplayConfigurationDto>> GetDefaultByEntityTypeNameAsync(
            string entityName,
            [FromQuery] bool includeDrafts = false)
        {
            try
            {
                var config = await _service.GetDefaultByEntityTypeNameAsync(entityName, includeDrafts);
                if (config == null)
                    return NotFound(new { message = $"No default DisplayConfiguration found for '{entityName}'." });

                return Ok(config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving default configuration for {EntityName}", entityName);
                return StatusCode(500, new { message = "An error occurred while retrieving the configuration." });
            }
        }

        /// <summary>
        /// Get all display configurations for entity type.
        /// </summary>
        [HttpGet("entity/{entityName}/all")]
        public async Task<ActionResult<IEnumerable<DisplayConfigurationDto>>> GetAllByEntityTypeNameAsync(
            string entityName,
            [FromQuery] bool includeDrafts = true)
        {
            try
            {
                var configs = await _service.GetAllByEntityTypeNameAsync(entityName, includeDrafts);
                return Ok(configs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving configurations for {EntityName}", entityName);
                return StatusCode(500, new { message = "An error occurred while retrieving configurations." });
            }
        }

        /// <summary>
        /// Get list of all entity type names with display configurations.
        /// </summary>
        [HttpGet("entity-names")]
        public async Task<ActionResult<IEnumerable<string>>> GetEntityTypeNamesAsync()
        {
            try
            {
                var names = await _service.GetEntityTypeNamesAsync();
                return Ok(names);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving entity type names");
                return StatusCode(500, new { message = "An error occurred while retrieving entity names." });
            }
        }

        /// <summary>
        /// Create new display configuration (starts as draft).
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<DisplayConfigurationDto>> CreateAsync(
            [FromBody] DisplayConfigurationDto config)
        {
            try
            {
                var created = await _service.CreateAsync(config);
                // Return Created with location header pointing to GetByIdAsync
                return Created(
                    $"/api/displayconfigurations/{created.Id}", 
                    created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating display configuration");
                return StatusCode(500, new { message = "An error occurred while creating the configuration." });
            }
        }

        /// <summary>
        /// Update existing display configuration.
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateAsync(int id, [FromBody] DisplayConfigurationDto config)
        {
            try
            {
                await _service.UpdateAsync(id, config);
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
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating display configuration {Id}", id);
                return StatusCode(500, new { message = "An error occurred while updating the configuration." });
            }
        }

        /// <summary>
        /// Delete display configuration.
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
                _logger.LogError(ex, "Error deleting display configuration {Id}", id);
                return StatusCode(500, new { message = "An error occurred while deleting the configuration." });
            }
        }

        /// <summary>
        /// Publish display configuration (sets IsDraft = false after validation).
        /// </summary>
        [HttpPost("{id:int}/publish")]
        public async Task<ActionResult<DisplayConfigurationDto>> PublishAsync(int id)
        {
            try
            {
                var result = await _service.PublishAsync(id);
                return Ok(result);
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
                _logger.LogError(ex, "Error publishing display configuration {Id}", id);
                return StatusCode(500, new { message = "An error occurred while publishing the configuration." });
            }
        }
    }
}
