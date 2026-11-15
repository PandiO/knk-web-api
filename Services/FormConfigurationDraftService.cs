using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// PandiO/knk-workspace#71: the form builder's draft and publish steps.
    /// <list type="bullet">
    /// <item>The rows (steps, fields) are always the <b>published</b> version: the form wizard, "New with", pickers,
    /// child forms and saved drafts keep reading them until Publish.</item>
    /// <item>The builder saves its working copy as <c>DraftJson</c> (no validation: a draft may be incomplete).</item>
    /// <item><b>Publish</b> applies the copy through <see cref="IFormConfigurationService.UpdateAsync"/> (every check
    /// a save ran before #71), moves the default when asked, and clears the copy.</item>
    /// <item>A configuration created in the builder or duplicated is <c>IsDraft</c> (never published) until its first
    /// Publish; the wizard never offers it. Rows from before #71 and imports are published.</item>
    /// <item>PandiO/knk-workspace#291: field validation rules are part of the copy (<c>validationRules</c>, keyed by field
    /// GUID, so a field that only exists in the draft can get rules); Publish adds, updates and deletes the published
    /// rules to match. A copy without the list (saved before #291) keeps the published rules as they are.</item>
    /// </list>
    /// </summary>
    public interface IFormConfigurationDraftService
    {
        Task<FormConfigurationBuilderStateDto?> GetBuilderStateAsync(int id);
        Task<FormConfigurationBuilderStateDto> CreateDraftAsync(FormConfigurationDto draft);
        Task<FormConfigurationBuilderStateDto> SaveDraftAsync(int id, FormConfigurationDto draft);
        Task<FormConfigurationBuilderStateDto> DiscardDraftAsync(int id);
        Task<FormConfigurationDto> PublishAsync(int id);
        Task<FormConfigurationBuilderStateDto> DuplicateAsync(int id, string? name);

        /// <summary>True when the configuration exists and was never published.</summary>
        Task<bool> IsNeverPublishedAsync(int id);

        /// <summary>After an edit outside the builder (Set default on Entity setup), the saved copy follows its default flag.</summary>
        Task SyncDraftDefaultAsync(int id, bool isDefault);
    }

    public class FormConfigurationDraftService : IFormConfigurationDraftService
    {
        private static readonly string[] OpenDraftStatuses = { "InProgress", "Paused" };

        private readonly KnKDbContext _db;
        private readonly IFormConfigurationRepository _repo;
        private readonly IFormConfigurationService _configurations;
        private readonly IFormConfigurationPortabilityService _portability;
        private readonly IMapper _mapper;

        public FormConfigurationDraftService(
            KnKDbContext db,
            IFormConfigurationRepository repo,
            IFormConfigurationService configurations,
            IFormConfigurationPortabilityService portability,
            IMapper mapper)
        {
            _db = db;
            _repo = repo;
            _configurations = configurations;
            _portability = portability;
            _mapper = mapper;
        }

        public async Task<FormConfigurationBuilderStateDto?> GetBuilderStateAsync(int id)
        {
            if (id <= 0) return null;
            var row = await _repo.GetByIdAsync(id);
            return row == null ? null : await BuildStateAsync(row);
        }

        public async Task<FormConfigurationBuilderStateDto> CreateDraftAsync(FormConfigurationDto draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (string.IsNullOrWhiteSpace(draft.EntityTypeName)) throw new ArgumentException("Choose an entity first.");
            var name = RequireName(draft.ConfigurationName);

            draft.Id = null;
            var row = new FormConfiguration
            {
                Name = name,
                EntityTypeName = draft.EntityTypeName.Trim(),
                Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description,
                IsDefault = false,
                IsDraft = true,
                StepOrderJson = "[]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            draft.EntityTypeName = row.EntityTypeName;
            draft.ConfigurationName = name;
            row.DraftJson = SerializeDraft(draft);
            row.DraftSavedAt = DateTime.UtcNow;

            _db.FormConfigurations.Add(row);
            await _db.SaveChangesAsync();
            return (await GetBuilderStateAsync(row.Id))!;
        }

        public async Task<FormConfigurationBuilderStateDto> SaveDraftAsync(int id, FormConfigurationDto draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var row = await _db.FormConfigurations.FirstOrDefaultAsync(c => c.Id == id)
                      ?? throw new KeyNotFoundException($"FormConfiguration with id {id} not found.");
            if (!string.Equals(row.EntityTypeName, draft.EntityTypeName?.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"This configuration is for {row.EntityTypeName}; its entity can't change.");
            var name = RequireName(draft.ConfigurationName);

            draft.Id = id.ToString();
            draft.EntityTypeName = row.EntityTypeName;
            draft.ConfigurationName = name;
            row.DraftJson = SerializeDraft(draft);
            row.DraftSavedAt = DateTime.UtcNow;
            if (row.IsDraft)
            {
                // Never published: the list on Entity setup shows the name and description being worked on.
                row.Name = name;
                row.Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description;
            }
            await _db.SaveChangesAsync();
            return (await GetBuilderStateAsync(id))!;
        }

        public async Task<FormConfigurationBuilderStateDto> DiscardDraftAsync(int id)
        {
            var row = await _db.FormConfigurations.Include(c => c.Steps).FirstOrDefaultAsync(c => c.Id == id)
                      ?? throw new KeyNotFoundException($"FormConfiguration with id {id} not found.");
            // A duplicate's rows are its first version: discarding goes back to them. A configuration made in the
            // builder has no rows to go back to.
            if (row.IsDraft && row.Steps.Count == 0)
                throw new InvalidOperationException(
                    $"'{row.Name}' was never published, so there is no published version to go back to. Delete it on Entity setup instead.");
            if (row.DraftJson != null)
            {
                row.DraftJson = null;
                row.DraftSavedAt = null;
                await _db.SaveChangesAsync();
            }
            return (await GetBuilderStateAsync(id))!;
        }

        public async Task<FormConfigurationDto> PublishAsync(int id)
        {
            var row = await _repo.GetByIdAsync(id)
                      ?? throw new KeyNotFoundException($"FormConfiguration with id {id} not found.");
            if (!row.IsDraft && row.DraftJson == null)
                throw new InvalidOperationException($"'{row.Name}' has no unpublished changes.");

            var working = WorkingCopy(row);
            if (working.Steps == null || working.Steps.Count == 0)
                throw new InvalidOperationException("Add at least one step before publishing.");
            if (working.Steps.Any(s => string.IsNullOrWhiteSpace(s.StepName)))
                throw new InvalidOperationException("Every step needs a name before publishing.");

            // #291: null = a copy saved before rules were part of it: the published rules stay as they are.
            var rules = working.ValidationRules;
            if (rules != null)
            {
                var ruleProblem = await DraftRulesProblemAsync(working, rules);
                if (ruleProblem != null) throw new InvalidOperationException($"Can't publish: {ruleProblem}");
            }

            var makeDefault = working.IsDefault;
            await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;

            // #291: rules dropped from the copy go first, and kept rules let go of their dependency until it is set
            // again below, so a field the copy removes (or moves to another step) isn't held by a rule (Restrict).
            if (rules != null) await PrepareRuleChangesAsync(row, rules);

            // Every check a builder save ran before #71 (metadata, many-to-many, conditions); a refusal changes nothing.
            try
            {
                await _configurations.UpdateAsync(id, working);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(ex.Message.Replace("Cannot update FormConfiguration:", "Can't publish:"), ex);
            }

            var published = await _db.FormConfigurations.FirstAsync(c => c.Id == id);
            if (makeDefault)
            {
                var others = await _db.FormConfigurations
                    .Where(c => c.EntityTypeName == published.EntityTypeName && c.IsDefault && c.Id != id)
                    .ToListAsync();
                foreach (var other in others)
                {
                    other.IsDefault = false;
                    other.UpdatedAt = DateTime.UtcNow;
                }
            }
            if (rules != null) await ApplyDraftRulesAsync(id, rules);
            published.IsDefault = makeDefault;
            published.IsDraft = false;
            published.DraftJson = null;
            published.DraftSavedAt = null;
            published.PublishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();

            return (await _configurations.GetByIdAsync(id))!;
        }

        public async Task<FormConfigurationBuilderStateDto> DuplicateAsync(int id, string? name)
        {
            var source = await _db.FormConfigurations.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new { c.Name, c.IsDraft, StepCount = c.Steps.Count })
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException($"FormConfiguration with id {id} not found.");
            if (source.StepCount == 0)
                throw new InvalidOperationException(
                    $"'{source.Name}' has no published version to copy yet. Publish it first, or keep editing it.");

            var copyName = ConfigurationDraftJson.CopyName(name, source.Name);
            var file = await _portability.ExportAsync(id)
                       ?? throw new KeyNotFoundException($"FormConfiguration with id {id} not found.");
            var json = ConfigurationDraftJson.WithNewGuids(
                FormConfigurationPortabilityService.Serialize(file),
                new[] { "steps", "childFormSteps", "fields" },
                new[] { "stepGuid", "fieldGuid" });
            var copy = System.Text.Json.JsonSerializer.Deserialize<PortableFormConfigurationDto>(json, FormConfigurationPortabilityService.JsonOptions)!;
            copy.Name = copyName;

            var result = await _portability.ImportDraftCopyAsync(copy);
            if (result.Status != FormConfigurationImportStatus.Imported || result.FormConfigurationId == null)
            {
                var why = result.Errors.Count > 0 ? string.Join(" ", result.Errors) : result.Message;
                throw new InvalidOperationException($"'{source.Name}' couldn't be copied: {why}");
            }
            return (await GetBuilderStateAsync(int.Parse(result.FormConfigurationId)))!;
        }

        public Task<bool> IsNeverPublishedAsync(int id) =>
            _db.FormConfigurations.AnyAsync(c => c.Id == id && c.IsDraft);

        public async Task SyncDraftDefaultAsync(int id, bool isDefault)
        {
            var row = await _db.FormConfigurations.FirstOrDefaultAsync(c => c.Id == id);
            if (row?.DraftJson == null) return;
            var draft = ConfigurationDraftJson.Deserialize<FormConfigurationDto>(row.DraftJson);
            if (draft == null || draft.IsDefault == isDefault) return;
            draft.IsDefault = isDefault;
            row.DraftJson = ConfigurationDraftJson.Serialize(draft);
            await _db.SaveChangesAsync();
        }

        // ------------------------------------------------------------------ helpers

        private FormConfigurationDto WorkingCopy(FormConfiguration row)
        {
            FormConfigurationDto? draft = null;
            if (row.DraftJson != null)
            {
                try
                {
                    draft = ConfigurationDraftJson.Deserialize<FormConfigurationDto>(row.DraftJson);
                }
                catch (System.Text.Json.JsonException)
                {
                    draft = null; // an unreadable copy: the builder opens the published version
                }
            }
            var working = draft ?? _mapper.Map<FormConfigurationDto>(row);
            working.Id = row.Id.ToString();
            working.EntityTypeName = row.EntityTypeName;
            working.Steps ??= new List<FormStepDto>();
            return working;
        }

        private async Task<FormConfigurationBuilderStateDto> BuildStateAsync(FormConfiguration row)
        {
            var working = WorkingCopy(row);
            working.IsDraft = row.IsDraft;
            working.HasUnpublishedChanges = row.DraftJson != null;
            working.DraftSavedAt = row.DraftSavedAt?.ToString("O");
            working.PublishedAt = row.PublishedAt?.ToString("O");
            working.CreatedAt = row.CreatedAt.ToString("O");
            working.UpdatedAt = row.UpdatedAt?.ToString("O");

            var openDrafts = await _db.FormSubmissionProgresses
                .CountAsync(p => p.FormConfigurationId == row.Id && OpenDraftStatuses.Contains(p.Status));
            var currentDefault = await _db.FormConfigurations.AsNoTracking()
                .Where(c => c.EntityTypeName == row.EntityTypeName && c.IsDefault && !c.IsDraft && c.Id != row.Id)
                .Select(c => new { c.Id, c.Name })
                .FirstOrDefaultAsync();

            // #291: the copy's rules; a copy without them (none saved yet, or saved before #291) shows the published ones.
            var publishedRules = await PublishedRulesAsync(row);
            working.ValidationRules ??= publishedRules;
            FormConfigurationDto? published = null;
            if (!row.IsDraft)
            {
                published = _mapper.Map<FormConfigurationDto>(row);
                published.ValidationRules = publishedRules;
            }

            return new FormConfigurationBuilderStateDto
            {
                Configuration = working,
                Published = published,
                IsPublished = !row.IsDraft,
                HasUnpublishedChanges = row.IsDraft || row.DraftJson != null,
                DraftSavedAt = row.DraftSavedAt?.ToString("O"),
                PublishedAt = row.PublishedAt?.ToString("O"),
                OpenDraftCount = openDrafts,
                CurrentDefaultId = currentDefault?.Id.ToString(),
                CurrentDefaultName = currentDefault?.Name
            };
        }

        // ------------------------------------------------------------------ #291: validation rules in the copy

        /// <summary>Every field of the published rows (steps and their child form steps).</summary>
        private static List<FormField> PublishedFields(FormConfiguration row)
        {
            var fields = new List<FormField>();
            void Visit(IEnumerable<FormStep> steps)
            {
                foreach (var step in steps)
                {
                    fields.AddRange(step.Fields);
                    Visit(step.ChildFormSteps);
                }
            }
            Visit(row.Steps);
            return fields.GroupBy(f => f.Id).Select(g => g.First()).ToList();
        }

        /// <summary>Every field of the working copy (steps and their child form steps).</summary>
        private static List<FormFieldDto> WorkingFields(FormConfigurationDto working)
        {
            var fields = new List<FormFieldDto>();
            void Visit(IEnumerable<FormStepDto>? steps)
            {
                foreach (var step in steps ?? Enumerable.Empty<FormStepDto>())
                {
                    fields.AddRange(step.Fields ?? new List<FormFieldDto>());
                    Visit(step.ChildFormSteps);
                }
            }
            Visit(working.Steps);
            return fields;
        }

        /// <summary>The published rules of the configuration's fields, in the copy's shape (field GUIDs).</summary>
        private async Task<List<FormDraftValidationRuleDto>> PublishedRulesAsync(FormConfiguration row)
        {
            var guidById = PublishedFields(row).ToDictionary(f => f.Id, f => f.FieldGuid);
            if (guidById.Count == 0) return new List<FormDraftValidationRuleDto>();
            var fieldIds = guidById.Keys.ToList();
            var rules = await _db.FieldValidationRules.AsNoTracking()
                .Where(r => fieldIds.Contains(r.FormFieldId))
                .OrderBy(r => r.Id)
                .ToListAsync();
            return rules.Select(r =>
            {
                var localDependency = r.DependsOnFieldId.HasValue && guidById.ContainsKey(r.DependsOnFieldId.Value);
                return new FormDraftValidationRuleDto
                {
                    Id = r.Id,
                    FieldGuid = guidById[r.FormFieldId].ToString(),
                    DependsOnFieldGuid = localDependency ? guidById[r.DependsOnFieldId!.Value].ToString() : null,
                    DependsOnFieldId = localDependency ? null : r.DependsOnFieldId,
                    ValidationType = r.ValidationType,
                    DependencyPath = r.DependencyPath,
                    ConfigJson = r.ConfigJson,
                    ErrorMessage = r.ErrorMessage,
                    SuccessMessage = r.SuccessMessage,
                    IsBlocking = r.IsBlocking,
                    RequiresDependencyFilled = r.RequiresDependencyFilled
                };
            }).ToList();
        }

        /// <summary>Why the copy's rules can't be published (null when they can): every reference must resolve.</summary>
        private async Task<string?> DraftRulesProblemAsync(FormConfigurationDto working, List<FormDraftValidationRuleDto> rules)
        {
            var byGuid = new Dictionary<Guid, FormFieldDto>();
            foreach (var field in WorkingFields(working))
            {
                if (Guid.TryParse(field.FieldGuid, out var guid)) byGuid.TryAdd(guid, field);
            }
            static string NameOf(FormFieldDto f) => string.IsNullOrWhiteSpace(f.Label) ? f.FieldName : f.Label;

            var dependsOn = new Dictionary<Guid, HashSet<Guid>>();
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                var type = string.IsNullOrWhiteSpace(rule.ValidationType) ? null : rule.ValidationType.Trim();
                if (!Guid.TryParse(rule.FieldGuid, out var fieldGuid) || !byGuid.TryGetValue(fieldGuid, out var field))
                    return $"validation rule {i + 1} ({type ?? "no type"}) belongs to a field that is no longer in the form. Delete the rule.";
                var where = $"the {type ?? "untyped"} rule on '{NameOf(field)}'";
                if (type == null) return $"{where} needs a validation type.";

                if (!string.IsNullOrWhiteSpace(rule.DependsOnFieldGuid))
                {
                    if (!Guid.TryParse(rule.DependsOnFieldGuid, out var dependency) || !byGuid.ContainsKey(dependency))
                        return $"{where} depends on a field that is no longer in the form. Edit or delete the rule.";
                    if (dependency == fieldGuid) return $"{where} can't depend on its own field.";
                    if (!dependsOn.TryGetValue(fieldGuid, out var targets)) dependsOn[fieldGuid] = targets = new HashSet<Guid>();
                    targets.Add(dependency);
                }
                else if (rule.DependsOnFieldId is int dependencyId && !await _db.FormFields.AnyAsync(f => f.Id == dependencyId))
                {
                    return $"{where} depends on a field of another form (id {dependencyId}) that no longer exists. Edit or delete the rule.";
                }
            }

            // Rules that depend on each other in a circle could never be checked (the old create/update refused them too).
            var state = new Dictionary<Guid, int>(); // 1 = visiting, 2 = done
            bool InCircle(Guid node)
            {
                if (state.TryGetValue(node, out var seen)) return seen == 1;
                state[node] = 1;
                if (dependsOn.TryGetValue(node, out var next) && next.Any(InCircle)) return true;
                state[node] = 2;
                return false;
            }
            var circular = dependsOn.Keys.FirstOrDefault(InCircle);
            if (circular != Guid.Empty)
                return $"validation rules depend on each other in a circle (starting at '{NameOf(byGuid[circular])}'). Remove one of the dependencies.";
            return null;
        }

        /// <summary>
        /// Before the rows change: deletes the published rules the copy no longer has, and clears the dependency of the
        /// kept ones (set again by <see cref="ApplyDraftRulesAsync"/>). Runs in Publish's transaction.
        /// </summary>
        private async Task PrepareRuleChangesAsync(FormConfiguration row, List<FormDraftValidationRuleDto> rules)
        {
            var fieldIds = PublishedFields(row).Select(f => f.Id).ToList();
            if (fieldIds.Count == 0) return;
            var kept = rules.Where(r => r.Id.HasValue).Select(r => r.Id!.Value).ToHashSet();
            var existing = await _db.FieldValidationRules.Where(r => fieldIds.Contains(r.FormFieldId)).ToListAsync();
            foreach (var rule in existing)
            {
                if (!kept.Contains(rule.Id)) _db.FieldValidationRules.Remove(rule);
                else rule.DependsOnFieldId = null;
            }
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// After the rows changed: the published rules become the copy's (by field GUID). A kept rule whose field was
        /// re-created (moved to another step) is created again.
        /// </summary>
        private async Task ApplyDraftRulesAsync(int id, List<FormDraftValidationRuleDto> rules)
        {
            var row = await _repo.GetByIdAsync(id)
                      ?? throw new KeyNotFoundException($"FormConfiguration with id {id} not found.");
            var fields = PublishedFields(row);
            var idByGuid = fields.GroupBy(f => f.FieldGuid).ToDictionary(g => g.Key, g => g.First().Id);
            var fieldIds = fields.Select(f => f.Id).ToList();
            var current = await _db.FieldValidationRules.Where(r => fieldIds.Contains(r.FormFieldId)).ToDictionaryAsync(r => r.Id);
            var used = new HashSet<int>();

            foreach (var rule in rules)
            {
                if (!idByGuid.TryGetValue(Guid.Parse(rule.FieldGuid!), out var fieldId))
                    throw new InvalidOperationException("Can't publish: a validation rule's field was not saved.");
                int? dependsOnId = rule.DependsOnFieldId;
                if (!string.IsNullOrWhiteSpace(rule.DependsOnFieldGuid))
                {
                    if (!idByGuid.TryGetValue(Guid.Parse(rule.DependsOnFieldGuid), out var localId))
                        throw new InvalidOperationException("Can't publish: a validation rule's dependency field was not saved.");
                    dependsOnId = localId;
                }

                FieldValidationRule target;
                if (rule.Id is int ruleId && current.TryGetValue(ruleId, out var existing) && used.Add(ruleId))
                {
                    target = existing;
                }
                else
                {
                    target = new FieldValidationRule { CreatedAt = DateTime.UtcNow };
                    _db.FieldValidationRules.Add(target);
                }
                target.FormFieldId = fieldId;
                target.DependsOnFieldId = dependsOnId;
                target.ValidationType = rule.ValidationType!.Trim();
                target.DependencyPath = string.IsNullOrWhiteSpace(rule.DependencyPath) ? null : rule.DependencyPath;
                target.ConfigJson = string.IsNullOrWhiteSpace(rule.ConfigJson) ? "{}" : rule.ConfigJson;
                target.ErrorMessage = rule.ErrorMessage ?? string.Empty;
                target.SuccessMessage = string.IsNullOrWhiteSpace(rule.SuccessMessage) ? null : rule.SuccessMessage;
                target.IsBlocking = rule.IsBlocking;
                target.RequiresDependencyFilled = rule.RequiresDependencyFilled;
            }
            await _db.SaveChangesAsync();
        }

        private static string RequireName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Give the configuration a name.");
            var trimmed = name.Trim();
            if (trimmed.Length > ConfigurationDraftJson.MaxNameLength)
                throw new ArgumentException($"The name may be at most {ConfigurationDraftJson.MaxNameLength} characters.");
            return trimmed;
        }

        private static string SerializeDraft(FormConfigurationDto draft)
        {
            // Publication state is the row's, never part of the copy.
            draft.IsDraft = false;
            draft.HasUnpublishedChanges = false;
            draft.DraftSavedAt = null;
            draft.PublishedAt = null;
            var json = ConfigurationDraftJson.Serialize(draft);
            if (json.Length > ConfigurationDraftJson.MaxDraftLength)
                throw new ArgumentException("The configuration is too large to save.");
            return json;
        }
    }
}
