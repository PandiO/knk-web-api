using System.Text.Json;
using knkwebapi_v2.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
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
        private readonly IDisplayConfigurationPortabilityService _portability;
        private readonly ILogger<DisplayConfigurationsController> _logger;

        public DisplayConfigurationsController(
            IDisplayConfigurationService service,
            IDisplayConfigurationPortabilityService portability,
            ILogger<DisplayConfigurationsController> logger)
        {
            _service = service;
            _portability = portability;
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
            catch (Exception ex) when (!DeleteConstraintExceptionFilter.IsConstraintFailure(ex))
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

        // ---- knk-displayconfig/1: portable export/import and bundled seeds (writes need knk.admin.content, class rule) ----

        /// <summary>The configuration as a portable "knk-displayconfig/1" file (GUIDs, no database ids).</summary>
        [HttpGet("{id:int}/export")]
        public async Task<IActionResult> Export(int id)
        {
            var file = await _portability.ExportAsync(id);
            if (file == null) return NotFound(new { message = $"DisplayConfiguration with id {id} not found." });
            return Content(DisplayConfigurationPortabilityService.Serialize(file), "application/json");
        }

        /// <summary>
        /// Imports a portable file as a new display configuration. Never overwrites: an existing configurationGuid is
        /// 409 AlreadyImported and writes nothing. dryRun=true only checks. makeDefault=true replaces the entity's
        /// default; otherwise the import is the default only when the entity has none. The import is published
        /// unless asDraft=true (the display page only shows a published default).
        /// </summary>
        [HttpPost("import")]
        public async Task<IActionResult> Import(
            [FromBody] JsonElement file,
            [FromQuery] bool dryRun = false,
            [FromQuery] bool makeDefault = false,
            [FromQuery] bool asDraft = false)
        {
            if (file.ValueKind != JsonValueKind.Object) return BadRequest(new { message = "The body must be a knk-displayconfig/1 JSON object." });
            return await RunImport(async () => await _portability.ImportJsonAsync(file.GetRawText(), dryRun, makeDefault, asDraft));
        }

        /// <summary>The bundled seed files (Data/DisplayConfigurationSeeds/) and whether each is imported.</summary>
        [HttpGet("seeds")]
        public async Task<IActionResult> GetSeeds()
        {
            return Ok(await _portability.ListSeedsAsync());
        }

        [HttpPost("seeds/{name}/import")]
        public async Task<IActionResult> ImportSeed(
            string name,
            [FromQuery] bool dryRun = false,
            [FromQuery] bool makeDefault = false,
            [FromQuery] bool asDraft = false)
        {
            return await RunImport(() => _portability.ImportSeedAsync(name, dryRun, makeDefault, asDraft), $"No bundled display seed named '{name}'.");
        }

        private async Task<IActionResult> RunImport(Func<Task<DisplayConfigurationImportResultDto?>> import, string? notFoundMessage = null)
        {
            DisplayConfigurationImportResultDto? result;
            try
            {
                result = await import();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "The database refused a display configuration import");
                return Conflict(new { message = "The database refused the import (was it imported at the same time?). Nothing was written; reload and check." });
            }

            if (result == null) return NotFound(new { message = notFoundMessage });
            return result.Status switch
            {
                FormConfigurationImportStatus.Imported => Created($"/api/displayconfigurations/{result.DisplayConfigurationId}", result),
                FormConfigurationImportStatus.DryRunPassed => Ok(result),
                FormConfigurationImportStatus.AlreadyImported => Conflict(result),
                _ => BadRequest(result)
            };
        }
    }
}
