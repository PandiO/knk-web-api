using knkwebapi_v2.Attributes;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Services;
using knkwebapi_v2.Dtos;
using System.Collections.Generic;

namespace knkwebapi_v2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrAnyAreaNode(ReadCategory.FormRuntime)]
    public class MetadataController : ControllerBase
    {
        private readonly IMetadataService _metadataService;

        public MetadataController(IMetadataService metadataService)
        {
            _metadataService = metadataService;
        }

        /// <summary>
        /// Get metadata for all form-configurable entities.
        /// Used by FormBuilder to populate entity dropdowns.
        /// </summary>
        [HttpGet("entities")]
        public ActionResult<List<EntityMetadataDto>> GetAllEntityMetadata()
        {
            var metadata = _metadataService.GetAllEntityMetadata();
            return Ok(metadata);
        }

        [HttpGet("entity-names")]
        public ActionResult<List<string>> GetEntityNames()
        {
            var entityNames = _metadataService.GetEntityNames();
            return Ok(entityNames);
        }

        /// <summary>
        /// Get metadata for a specific entity by name.
        /// Used by FormBuilder to show available fields for an entity.
        /// </summary>
        [HttpGet("entities/{entityTypeName}")]
        public ActionResult<EntityMetadataDto> GetEntityMetadata(string entityTypeName)
        {
            var metadata = _metadataService.GetEntityMetadata(entityTypeName);
            if (metadata == null)
            {
                return NotFound($"Entity '{entityTypeName}' not found or not marked as form-configurable.");
            }
            return Ok(metadata);
        }
    }
}
