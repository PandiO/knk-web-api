using knkwebapi_v2.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Dtos;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/field-validation-rules")]
    // Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.content or the game server; reads stay logged-in.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageContent)]
    public class FieldValidationRulesController : ControllerBase
    {
        private readonly IValidationService _validationService;
        private readonly IFieldValidationRuleService _ruleService;
        private readonly IPlaceholderResolutionService _placeholderService;
        private readonly IDependencyResolutionService _dependencyService;
        private readonly IPathResolutionService _pathService;

        public FieldValidationRulesController(
            IValidationService validationService,
            IFieldValidationRuleService ruleService,
            IPlaceholderResolutionService placeholderService,
            IDependencyResolutionService dependencyService,
            IPathResolutionService pathService)
        {
            _validationService = validationService;
            _ruleService = ruleService;
            _placeholderService = placeholderService;
            _dependencyService = dependencyService;
            _pathService = pathService;
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var rule = await _ruleService.GetByIdAsync(id);
            if (rule == null) return NotFound();
            return Ok(rule);
        }

        [HttpGet("by-field/{fieldId:int}")]
        public async Task<IActionResult> GetByFormField(int fieldId)
        {
            var rules = await _ruleService.GetByFormFieldIdAsync(fieldId);
            return Ok(rules);
        }

        [HttpGet("by-configuration/{configId:int}")]
        public async Task<IActionResult> GetByConfiguration(int configId)
        {
            var rules = await _ruleService.GetByFormConfigurationIdAsync(configId);
            return Ok(rules);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateFieldValidationRuleDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _ruleService.CreateAsync(dto);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateFieldValidationRuleDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _ruleService.UpdateAsync(id, dto);
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
                await _ruleService.DeleteAsync(id);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost("validate")]
        public async Task<IActionResult> ValidateField([FromBody] ValidateFieldRequestDto request)
        {
            if (request == null) return BadRequest();
            try
            {
                var result = await _validationService.ValidateFieldAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Validation execution failed", error = ex.Message });
            }
        }

        /// <summary>
        /// Resolve placeholder values for a validation rule or explicit placeholder list.
        /// </summary>
        /// <remarks>
        /// Placeholder syntax examples:
        /// - {Name}
        /// - {Town.Name}
        /// - {District.Town.Name}
        /// - {Town.Districts.Count}
        /// </remarks>
        /// <param name="request">Placeholder resolution request payload.</param>
        /// <response code="200">Returns resolved placeholders and resolution errors (if any).</response>
        /// <response code="400">Invalid request (missing rule ID and placeholder paths).</response>
        /// <response code="404">Rule not found.</response>
        [HttpPost("resolve-placeholders")]
        public async Task<IActionResult> ResolvePlaceholders([FromBody] PlaceholderResolutionRequest request)
        {
            if (request == null) return BadRequest();

            if (!request.FieldValidationRuleId.HasValue
                && (request.PlaceholderPaths == null || !request.PlaceholderPaths.Any()))
            {
                return BadRequest(new { message = "FieldValidationRuleId or PlaceholderPaths must be provided." });
            }

            if (request.FieldValidationRuleId.HasValue)
            {
                var rule = await _ruleService.GetByIdAsync(request.FieldValidationRuleId.Value);
                if (rule == null) return NotFound();
            }

            try
            {
                var response = await _placeholderService.ResolveAllLayersAsync(request);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Placeholder resolution failed", error = ex.Message });
            }
        }

        /// <summary>
        /// Validate a field value against a specific validation rule.
        /// </summary>
        /// <remarks>
        /// This endpoint resolves placeholders and executes the rule-specific validation logic.
        /// </remarks>
        /// <param name="request">Validation request payload.</param>
        /// <response code="200">Validation result with message template and placeholders.</response>
        /// <response code="400">Invalid request.</response>
        /// <response code="404">Rule not found.</response>
        [HttpPost("/api/field-validations/validate-field")]
        public async Task<IActionResult> ValidateFieldRule([FromBody] ValidateFieldRuleRequestDto request)
        {
            if (request == null) return BadRequest();
            if (!request.FieldValidationRuleId.HasValue || request.FieldValidationRuleId.Value <= 0)
            {
                return BadRequest(new { message = "FieldValidationRuleId is required." });
            }

            var rule = await _ruleService.GetByIdAsync(request.FieldValidationRuleId.Value);
            if (rule == null) return NotFound();

            try
            {
                // Use the main validation service with field ID to validate
                // This ensures proper multi-rule aggregation and placeholder resolution
                var result = await _validationService.ValidateFieldAsync(
                    rule.FormFieldId,
                    request.FieldValue,
                    request.DependencyFieldValue,
                    null);  // Note: CurrentEntityPlaceholders not converted to formContextData

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Validation execution failed", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all placeholder paths used by a validation rule's error and success messages.
        /// </summary>
        /// <param name="ruleId">Validation rule ID.</param>
        /// <response code="200">List of placeholder paths.</response>
        /// <response code="404">Rule not found.</response>
        [HttpGet("/api/field-validations/rules/{ruleId:int}/placeholders")]
        public async Task<IActionResult> GetPlaceholdersByRule(int ruleId)
        {
            var rule = await _ruleService.GetByIdAsync(ruleId);
            if (rule == null) return NotFound();

            try
            {
                var placeholders = new HashSet<string>();

                if (!string.IsNullOrWhiteSpace(rule.ErrorMessage))
                {
                    var errorPlaceholders = await _placeholderService.ExtractPlaceholdersAsync(rule.ErrorMessage);
                    foreach (var placeholder in errorPlaceholders)
                    {
                        placeholders.Add(placeholder);
                    }
                }

                if (!string.IsNullOrWhiteSpace(rule.SuccessMessage))
                {
                    var successPlaceholders = await _placeholderService.ExtractPlaceholdersAsync(rule.SuccessMessage);
                    foreach (var placeholder in successPlaceholders)
                    {
                        placeholders.Add(placeholder);
                    }
                }

                return Ok(placeholders.ToList());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Placeholder extraction failed", error = ex.Message });
            }
        }

        [HttpGet("health-check/configuration/{configId:int}")]
        public async Task<IActionResult> ValidateConfigurationHealth(int configId)
        {
            try
            {
                var issues = await _ruleService.ValidateConfigurationHealthAsync(configId);
                return Ok(issues);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost("health-check/configuration/draft")]
        public async Task<IActionResult> ValidateDraftConfiguration([FromBody] FormConfigurationDto configDto)
        {
            try
            {
                if (configDto == null)
                {
                    return BadRequest(new { message = "Configuration data is required" });
                }

                var issues = await _ruleService.ValidateDraftConfigurationAsync(configDto);
                return Ok(issues);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Draft validation failed", error = ex.Message });
            }
        }

        /// <summary>
        /// Batch resolve all dependencies for validation rules on specified fields.
        /// </summary>
        [HttpPost("resolve-dependencies")]
        public async Task<ActionResult<DependencyResolutionResponse>> ResolveDependencies(
            [FromBody] DependencyResolutionRequest request)
        {
            if (request?.FieldIds == null || request.FieldIds.Length == 0)
            {
                return BadRequest("FieldIds cannot be empty");
            }

            var response = await _dependencyService.ResolveDependenciesAsync(request);
            return Ok(response);
        }

        /// <summary>
        /// Validate a dependency path syntax and entity compatibility.
        /// </summary>
        [HttpPost("validate-path")]
        public async Task<ActionResult<PathValidationResult>> ValidatePath(
            [FromBody] ValidatePathRequest request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.Path)
                || string.IsNullOrWhiteSpace(request.EntityTypeName))
            {
                return BadRequest("Path and EntityTypeName are required");
            }

            var result = await _pathService.ValidatePathAsync(request.EntityTypeName, request.Path);
            return Ok(result);
        }

        /// <summary>
        /// Get all properties of an entity for UI suggestions.
        /// </summary>
        [HttpGet("entity/{entityName}/properties")]
        public async Task<ActionResult<List<EntityPropertySuggestion>>> GetEntityProperties(string entityName)
        {
            if (string.IsNullOrWhiteSpace(entityName))
            {
                return BadRequest("Entity name is required");
            }

            var properties = await _pathService.GetEntityPropertiesAsync(entityName);
            return Ok(properties);
        }
    }
}