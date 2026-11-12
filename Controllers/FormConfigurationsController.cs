using knkwebapi_v2.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Services;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Repositories;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.content or the game server; reads stay logged-in.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageContent)]
    [RequireServiceOrAnyAreaNode(ReadCategory.FormRuntime)]
    public class FormConfigurationsController : ControllerBase
    {
        private readonly IFormConfigurationService _service;
        private readonly IFormTemplateReusableService _reusableService;
        private readonly IFormStepRepository _stepRepo;
        private readonly IFormFieldRepository _fieldRepo;
        private readonly IFormConfigurationRepository _configRepo;
        private readonly IFormStepService _stepService;
        private readonly IFormFieldService _fieldService;
        private readonly IFormConfigurationPortabilityService _portability;
        private readonly IFormConfigurationDraftService _drafts;

        public FormConfigurationsController(
            IFormConfigurationService service,
            IFormTemplateReusableService reusableService,
            IFormStepRepository stepRepo,
            IFormFieldRepository fieldRepo,
            IFormConfigurationRepository configRepo,
            IFormStepService stepService,
            IFormFieldService fieldService,
            IFormConfigurationPortabilityService portability,
            IFormConfigurationDraftService drafts)
        {
            _service = service;
            _reusableService = reusableService;
            _stepRepo = stepRepo;
            _fieldRepo = fieldRepo;
            _configRepo = configRepo;
            _stepService = stepService;
            _fieldService = fieldService;
            _portability = portability;
            _drafts = drafts;
        }

        /// <param name="includeDrafts">#71: also configurations that were never published (Entity setup's list).</param>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool includeDrafts = false)
        {
            var configs = await _service.GetAllAsync();
            return Ok(WithoutDrafts(configs, includeDrafts));
        }

        // #71: the wizard, "New with", child forms and sub-configuration pickers only ever see published configurations.
        private static IEnumerable<FormConfigurationDto> WithoutDrafts(IEnumerable<FormConfigurationDto> configs, bool includeDrafts) =>
            includeDrafts ? configs : configs.Where(c => !c.IsDraft).ToList();

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var config = await _service.GetByIdAsync(id);
            if (config == null) return NotFound();
            return Ok(config);
        }

        [HttpGet("{entityName}")]
        public async Task<IActionResult> GetAllByEntityTypeName(string entityName, bool defaultOnly = false, [FromQuery] bool includeDrafts = false)
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
                    return Ok(WithoutDrafts(config, includeDrafts));
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
        public async Task<IActionResult> GetAllByEntityTypeNameAll(string entityName, [FromQuery] bool includeDrafts = false)
        {
            var configs = await _service.GetAllByEntityTypeNameAllAsync(entityName);
            return Ok(WithoutDrafts(configs, includeDrafts));
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
                // #71: a never-published configuration can't be the wizard's default before its first Publish.
                if (configDto.IsDefault && await _drafts.IsNeverPublishedAsync(id))
                    return BadRequest("Publish this configuration in the builder before making it the default.");
                await _service.UpdateAsync(id, configDto);
                await _drafts.SyncDraftDefaultAsync(id, configDto.IsDefault);
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

        // ---- PandiO/knk-workspace#71: the builder's draft, Publish and Duplicate (writes need knk.admin.content, class rule) ----

        /// <summary>
        /// What the builder opens: the working copy (saved draft, else the published version), the published version,
        /// and what Publish would change (open wizard drafts, the current default).
        /// </summary>
        [HttpGet("{id:int}/builder")]
        [DashboardReads(DashboardArea.Content)]
        public async Task<IActionResult> GetBuilderState(int id)
        {
            var state = await _drafts.GetBuilderStateAsync(id);
            if (state == null) return NotFound();
            return Ok(state);
        }

        /// <summary>A new configuration, saved as a draft only (never published, the wizard doesn't offer it).</summary>
        [HttpPost("drafts")]
        public Task<IActionResult> CreateDraft([FromBody] FormConfigurationDto draft) =>
            RunDraft(async () =>
            {
                var state = await _drafts.CreateDraftAsync(draft);
                return CreatedAtAction(nameof(GetBuilderState), new { id = int.Parse(state.Configuration.Id!) }, state);
            });

        /// <summary>Saves the builder's working copy; the published version (and the wizard) stay as they are.</summary>
        [HttpPut("{id:int}/draft")]
        public Task<IActionResult> SaveDraft(int id, [FromBody] FormConfigurationDto draft) =>
            RunDraft(async () => Ok(await _drafts.SaveDraftAsync(id, draft)));

        /// <summary>Drops the saved working copy; the builder opens the published version again.</summary>
        [HttpPost("{id:int}/draft/discard")]
        public Task<IActionResult> DiscardDraft(int id) =>
            RunDraft(async () => Ok(await _drafts.DiscardDraftAsync(id)));

        /// <summary>Applies the working copy with every save check; from then on the wizard uses it.</summary>
        [HttpPost("{id:int}/publish")]
        public Task<IActionResult> Publish(int id) =>
            RunDraft(async () => Ok(await _drafts.PublishAsync(id)));

        /// <summary>A copy of the published version (steps, fields, conditions, validation rules) as a new draft.</summary>
        [HttpPost("{id:int}/duplicate")]
        public Task<IActionResult> Duplicate(int id, [FromBody] DuplicateConfigurationRequestDto? request) =>
            RunDraft(async () => Ok(await _drafts.DuplicateAsync(id, request?.Name)));

        private async Task<IActionResult> RunDraft(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
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

        // ---- KNG-138: portable export/import and bundled seeds (writes need knk.admin.content, class rule) ----

        /// <summary>The configuration as a portable "knk-formconfig/1" file (GUIDs, no database ids).</summary>
        [HttpGet("{id:int}/export")]
        [DashboardReads(DashboardArea.Content)]
        public async Task<IActionResult> Export(int id)
        {
            var file = await _portability.ExportAsync(id);
            if (file == null) return NotFound();
            return Content(FormConfigurationPortabilityService.Serialize(file), "application/json");
        }

        /// <summary>
        /// Imports a portable file as a new configuration. Never overwrites: an existing configurationGuid
        /// is 409 AlreadyImported and writes nothing. dryRun=true only checks. makeDefault=true replaces
        /// the entity's default; otherwise the import is the default only when the entity has none.
        /// </summary>
        [HttpPost("import")]
        public async Task<IActionResult> Import([FromBody] JsonElement file, [FromQuery] bool dryRun = false, [FromQuery] bool makeDefault = false)
        {
            if (file.ValueKind != JsonValueKind.Object) return BadRequest("The body must be a knk-formconfig/1 JSON object.");
            return await RunImport(async () => await _portability.ImportJsonAsync(file.GetRawText(), dryRun, makeDefault));
        }

        /// <summary>The bundled seed files (Data/FormConfigurationSeeds/) and whether each is imported.</summary>
        [HttpGet("seeds")]
        [DashboardReads(DashboardArea.Content)]
        public async Task<IActionResult> GetSeeds()
        {
            return Ok(await _portability.ListSeedsAsync());
        }

        [HttpPost("seeds/{name}/import")]
        public async Task<IActionResult> ImportSeed(string name, [FromQuery] bool dryRun = false, [FromQuery] bool makeDefault = false)
        {
            return await RunImport(() => _portability.ImportSeedAsync(name, dryRun, makeDefault), $"No bundled seed named '{name}'.");
        }

        private async Task<IActionResult> RunImport(Func<Task<FormConfigurationImportResultDto?>> import, string? notFoundMessage = null)
        {
            FormConfigurationImportResultDto? result;
            try
            {
                result = await import();
            }
            catch (DbUpdateException)
            {
                // e.g. the same file imported twice at once: the unique configurationGuid index refuses the second.
                return Conflict(new { message = "The database refused the import (was it imported at the same time?). Nothing was written; reload and check." });
            }

            if (result == null) return NotFound(notFoundMessage);
            return result.Status switch
            {
                FormConfigurationImportStatus.Imported => CreatedAtAction(nameof(GetById), new { id = int.Parse(result.FormConfigurationId!) }, result),
                FormConfigurationImportStatus.DryRunPassed => Ok(result),
                FormConfigurationImportStatus.AlreadyImported => Conflict(result),
                _ => BadRequest(result)
            };
        }
    }
}
