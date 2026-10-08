using knkwebapi_v2.Attributes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Services;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Repositories;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.content or the game server; reads stay logged-in.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageContent)]
    public class FormConfigurationsController : ControllerBase
    {
        private readonly IFormConfigurationService _service;
        private readonly IFormTemplateReusableService _reusableService;
        private readonly IFormStepRepository _stepRepo;
        private readonly IFormFieldRepository _fieldRepo;
        private readonly IFormConfigurationRepository _configRepo;
        private readonly IFormStepService _stepService;
        private readonly IFormFieldService _fieldService;

        public FormConfigurationsController(
            IFormConfigurationService service,
            IFormTemplateReusableService reusableService,
            IFormStepRepository stepRepo,
            IFormFieldRepository fieldRepo,
            IFormConfigurationRepository configRepo,
            IFormStepService stepService,
            IFormFieldService fieldService)
        {
            _service = service;
            _reusableService = reusableService;
            _stepRepo = stepRepo;
            _fieldRepo = fieldRepo;
            _configRepo = configRepo;
            _stepService = stepService;
            _fieldService = fieldService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var configs = await _service.GetAllAsync();
            return Ok(configs);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var config = await _service.GetByIdAsync(id);
            if (config == null) return NotFound();
            return Ok(config);
        }

        [HttpGet("{entityName}")]
        public async Task<IActionResult> GetAllByEntityTypeName(string entityName, bool defaultOnly = false)
        {
            try
            {
                if (defaultOnly)
                {
                    var config = await _service.GetDefaultByEntityTypeNameAsync(entityName);
                    if (config == null) return NotFound();
                    return Ok(config);
                }
                else
                {
                    var config = await _service.GetAllByEntityTypeNameAsync(entityName, defaultOnly);
                    if (config == null) return NotFound();
                    return Ok(config);
                }
            }
            catch (KeyNotFoundException)
            {
                // No default configuration found for this entity type
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{entityName}/all")]
        public async Task<IActionResult> GetAllByEntityTypeNameAll(string entityName)
        {
            var configs = await _service.GetAllByEntityTypeNameAllAsync(entityName);
            return Ok(configs);
        }

        [HttpGet("entity-names")]
        public async Task<IActionResult> GetEntityNames()
        {
            var entityNames = await _service.GetEntityTypeNamesAsync();
            return Ok(entityNames);
        }

        /// <summary>
        /// Get all reusable step templates from the library.
        /// </summary>
        [HttpGet("reusable-steps")]
        public async Task<IActionResult> GetReusableSteps()
        {
            var steps = await _stepService.GetAllReusableAsync();
            return Ok(steps);
        }

        /// <summary>
        /// Get all reusable field templates from the library.
        /// </summary>
        [HttpGet("reusable-fields")]
        public async Task<IActionResult> GetReusableFields()
        {
            var fields = await _fieldService.GetAllReusableAsync();
            return Ok(fields);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] FormConfigurationDto configDto)
        {
            if (configDto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(configDto);
                return CreatedAtAction(nameof(GetById), new { id = int.Parse(created.Id!) }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] FormConfigurationDto configDto)
        {
            if (configDto == null) return BadRequest();
            try
            {
                await _service.UpdateAsync(id, configDto);
                // Return the updated resource instead of 204
                var updated = await _service.GetByIdAsync(id);
                return Ok(updated);
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

        /// <summary>
        /// Add a reusable step template to a configuration (copy or link mode).
        /// POST /api/form-configurations/{configId}/steps/add-from-template
        /// </summary>
        [HttpPost("{configId:int}/steps/add-from-template")]
        public async Task<IActionResult> AddReusableStepToConfiguration(
            int configId,
            [FromBody] AddReusableStepRequest request)
        {
            if (request == null)
                return BadRequest("Request body is required.");

            try
            {
                var linkMode = request.LinkMode.Equals("link", StringComparison.OrdinalIgnoreCase)
                    ? ReuseLinkMode.Link
                    : ReuseLinkMode.Copy;

                var newStep = await _reusableService.AddReusableStepToConfigurationAsync(
                    configId,
                    request.SourceStepId,
                    linkMode,
                    _configRepo,
                    _stepRepo);

                return Ok(newStep);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Add a reusable field template to a step (copy or link mode).
        /// POST /api/form-configurations/steps/{stepId}/fields/add-from-template
        /// </summary>
        [HttpPost("steps/{stepId:int}/fields/add-from-template")]
        public async Task<IActionResult> AddReusableFieldToStep(
            int stepId,
            [FromBody] AddReusableFieldRequest request)
        {
            if (request == null)
                return BadRequest("Request body is required.");

            try
            {
                var linkMode = request.LinkMode.Equals("link", StringComparison.OrdinalIgnoreCase)
                    ? ReuseLinkMode.Link
                    : ReuseLinkMode.Copy;

                var newField = await _reusableService.AddReusableFieldToStepAsync(
                    stepId,
                    request.SourceFieldId,
                    linkMode,
                    _stepRepo,
                    _fieldRepo);

                return Ok(newField);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
