using knkwebapi_v2.Attributes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.content or the game server; reads stay logged-in.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageContent)]
    public class EntityTypeConfigurationController : ControllerBase
    {
        private readonly IEntityTypeConfigurationService _service;

        public EntityTypeConfigurationController(IEntityTypeConfigurationService service)
        {
            _service = service;
        }

        /// <summary>
        /// Get configuration by ID.
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(EntityTypeConfigurationReadDto), 200)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<EntityTypeConfigurationReadDto>> GetById(string id)
        {
            var config = await _service.GetByIdAsync(id);
            if (config == null)
                return NotFound();
            
            return Ok(config);
        }

        /// <summary>
        /// Get configuration for a specific entity type.
        /// Returns 404 if no configuration exists (entity uses defaults).
        /// </summary>
        [HttpGet("by-entity/{entityTypeName}")]
        [ProducesResponseType(typeof(EntityTypeConfigurationReadDto), 200)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<EntityTypeConfigurationReadDto>> GetByEntityTypeName(string entityTypeName)
        {
            var config = await _service.GetByEntityTypeNameAsync(entityTypeName);
            if (config == null)
                return NotFound();
            
            return Ok(config);
        }

        /// <summary>
        /// Get all entity type configurations.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<EntityTypeConfigurationReadDto>), 200)]
        public async Task<ActionResult<List<EntityTypeConfigurationReadDto>>> GetAll()
        {
            var configs = await _service.GetAllAsync();
            return Ok(configs);
        }

        /// <summary>
        /// Get merged metadata for a specific entity type.
        /// Combines base EntityMetadata with configuration properties.
        /// </summary>
        [HttpGet("merged/{entityTypeName}")]
        [ProducesResponseType(typeof(MergedEntityMetadataDto), 200)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<MergedEntityMetadataDto>> GetMergedMetadata(string entityTypeName)
        {
            try
            {
                var merged = await _service.GetMergedMetadataByEntityTypeAsync(entityTypeName);
                return Ok(merged);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        /// <summary>
        /// Get all merged metadata.
        /// Combines all EntityMetadata with all configurations.
        /// </summary>
        [HttpGet("merged/all")]
        [ProducesResponseType(typeof(List<MergedEntityMetadataDto>), 200)]
        public async Task<ActionResult<List<MergedEntityMetadataDto>>> GetAllMergedMetadata()
        {
            var merged = await _service.GetAllMergedMetadataAsync();
            return Ok(merged);
        }

        /// <summary>
        /// Create a new entity type configuration.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(EntityTypeConfigurationReadDto), 201)]
        [ProducesResponseType(400)]
        public async Task<ActionResult<EntityTypeConfigurationReadDto>> Create(EntityTypeConfigurationCreateDto dto)
        {
            try
            {
                var config = await _service.CreateAsync(dto);
                return CreatedAtAction(nameof(GetById), new { id = config.Id }, config);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Update an existing entity type configuration.
        /// </summary>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(EntityTypeConfigurationReadDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<EntityTypeConfigurationReadDto>> Update(string id, EntityTypeConfigurationUpdateDto dto)
        {
            if (id != dto.Id)
                return BadRequest(new { message = "ID mismatch" });

            try
            {
                var config = await _service.UpdateAsync(dto);
                return Ok(config);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Delete an entity type configuration.
        /// Entity will revert to default display settings.
        /// </summary>
        [HttpDelete("{id}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Delete(string id)
        {
            var success = await _service.DeleteAsync(id);
            if (!success)
                return NotFound();
            
            return NoContent();
        }
    }
}
