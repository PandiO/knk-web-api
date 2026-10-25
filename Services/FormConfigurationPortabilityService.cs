using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>Where the bundled seed files live (Data/FormConfigurationSeeds/ under the content root).</summary>
    public sealed class FormConfigurationSeedOptions
    {
        public string Directory { get; init; } = string.Empty;
    }

    /// <summary>
    /// KNG-138 (spec DEFAULT_FORM_SEEDS.md §3). Export writes a configuration as a portable file;
    /// import builds a new configuration from one. Import rules: never overwrite (an existing
    /// configurationGuid writes nothing), validate with FormTemplateValidationService, resolve every
    /// GUID reference before writing, create validation rules after the fields exist, and set
    /// IsDefault only when the entity has no default or makeDefault is asked.
    /// </summary>
    public class FormConfigurationPortabilityService : IFormConfigurationPortabilityService
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>The web app's predefined world-task types (FieldEditor PREDEFINED_TASK_TYPES), legacy spelling included.</summary>
        public static readonly IReadOnlySet<string> KnownWorldTaskTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ReagionCreate", "LocationSelection", "LocationCapture", "RegionClaim", "VerifyLocation",
            "VerifyStructure", "VerifyPlacement", "VerifyResource", "VerifyBoundary", "GateBlockScan",
            "GateOpenedBlockScan", "ItemScan", "KitScan", "Custom"
        };

        private static readonly Regex SeedNamePattern = new("^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.Compiled);

        private readonly KnKDbContext _db;
        private readonly IFormConfigurationRepository _repo;
        private readonly IFormTemplateValidationService _validationService;
        private readonly IMetadataService _metadataService;
        private readonly FormConfigurationSeedOptions _seedOptions;
        private Dictionary<Guid, string>? _seedNamesByGuid;

        public FormConfigurationPortabilityService(
            KnKDbContext db,
            IFormConfigurationRepository repo,
            IFormTemplateValidationService validationService,
            IMetadataService metadataService,
            FormConfigurationSeedOptions seedOptions)
        {
            _db = db;
            _repo = repo;
            _validationService = validationService;
            _metadataService = metadataService;
            _seedOptions = seedOptions;
        }

        public static string Serialize(PortableFormConfigurationDto file) => JsonSerializer.Serialize(file, JsonOptions);

        // ---------------------------------------------------------------- export

        public async Task<PortableFormConfigurationDto?> ExportAsync(int id)
        {
            var config = await _repo.GetByIdAsync(id);
            if (config == null) return null;

            var notes = new List<string>();
            var allSteps = config.Steps.Concat(config.Steps.SelectMany(s => s.ChildFormSteps)).ToList();
            var allFields = allSteps.SelectMany(s => s.Fields).ToList();

            var subConfigIds = allSteps.Select(s => s.SubConfigurationId)
                .Concat(allFields.Select(f => f.SubConfigurationId))
                .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
            var refsById = await _db.FormConfigurations.AsNoTracking()
                .Where(c => subConfigIds.Contains(c.Id))
                .Select(c => new PortableRefRow(c.Id, c.ConfigurationGuid, c.EntityTypeName, c.Name))
                .ToDictionaryAsync(c => c.Id);

            var file = new PortableFormConfigurationDto
            {
                Format = PortableFormConfigurationDto.CurrentFormat,
                ConfigurationGuid = config.ConfigurationGuid.ToString(),
                EntityTypeName = config.EntityTypeName,
                Name = config.Name,
                Description = config.Description,
                Steps = FormOrdering.OrderSteps(config).Select(s => ExportStep(s, refsById, notes)).ToList()
            };

            var guidByFieldId = allFields.ToDictionary(f => f.Id, f => f.FieldGuid);
            var fieldIds = guidByFieldId.Keys.ToList();
            var rules = await _db.FieldValidationRules.AsNoTracking()
                .Where(r => fieldIds.Contains(r.FormFieldId))
                .Include(r => r.DependsOnField!).ThenInclude(f => f.FormStep!).ThenInclude(s => s.FormConfiguration)
                .OrderBy(r => r.Id)
                .ToListAsync();

            foreach (var rule in rules)
            {
                PortableFieldRefDto? dependsOn = null;
                if (rule.DependsOnFieldId.HasValue)
                {
                    if (guidByFieldId.TryGetValue(rule.DependsOnFieldId.Value, out var localGuid))
                    {
                        dependsOn = new PortableFieldRefDto { FieldGuid = localGuid.ToString(), FieldName = rule.DependsOnField?.FieldName };
                    }
                    else
                    {
                        var owner = rule.DependsOnField?.FormStep?.FormConfiguration;
                        dependsOn = new PortableFieldRefDto
                        {
                            FieldGuid = rule.DependsOnField?.FieldGuid.ToString(),
                            ConfigurationGuid = owner?.ConfigurationGuid.ToString(),
                            FieldName = rule.DependsOnField?.FieldName
                        };
                        if (owner == null)
                        {
                            notes.Add($"Validation rule {rule.Id} depends on field {rule.DependsOnFieldId} outside any configuration; its dependsOn will not resolve on import.");
                        }
                    }
                }

                file.ValidationRules.Add(new PortableValidationRuleDto
                {
                    FieldGuid = guidByFieldId[rule.FormFieldId].ToString(),
                    ValidationType = rule.ValidationType,
                    DependsOn = dependsOn,
                    DependencyPath = rule.DependencyPath,
                    ConfigJson = rule.ConfigJson,
                    ErrorMessage = rule.ErrorMessage,
                    SuccessMessage = rule.SuccessMessage,
                    IsBlocking = rule.IsBlocking,
                    RequiresDependencyFilled = rule.RequiresDependencyFilled
                });
            }

            file.Notes = notes.Count > 0 ? notes : null;
            return file;
        }

        private sealed record PortableRefRow(int Id, Guid ConfigurationGuid, string EntityTypeName, string Name);

        private static PortableFormStepDto ExportStep(FormStep step, IReadOnlyDictionary<int, PortableRefRow> refsById, List<string> notes)
        {
            if (step.IsLinkedToSource)
                notes.Add($"Step '{step.StepName}' is linked to a reusable step; exported as a copy (imports are never linked).");

            return new PortableFormStepDto
            {
                StepGuid = step.StepGuid.ToString(),
                StepName = step.StepName,
                Description = step.Description,
                IsManyToManyRelationship = step.IsManyToManyRelationship,
                RelatedEntityPropertyName = step.RelatedEntityPropertyName,
                JoinEntityType = step.JoinEntityType,
                SubConfigurationRef = ExportRef(step.SubConfigurationId, $"Step '{step.StepName}'", refsById, notes),
                Conditions = step.StepConditions.Count == 0 ? null : step.StepConditions.OrderBy(c => c.Id).Select(c => new PortableStepConditionDto
                {
                    ConditionType = c.ConditionType == "CompletionCondition" ? "Completion" : "Entry",
                    ConditionJson = c.ConditionLogicJson,
                    ErrorMessage = c.ErrorMessage,
                    IsActive = c.IsActive ? null : false
                }).ToList(),
                DisplayConditionGroups = ExportGroups(step.DisplayConditionGroups),
                Fields = FormOrdering.OrderFields(step).Select(f => ExportField(f, step, refsById, notes)).ToList(),
                ChildFormSteps = step.ChildFormSteps.Count == 0
                    ? null
                    : step.ChildFormSteps.OrderBy(c => c.Id).Select(c => ExportStep(c, refsById, notes)).ToList()
            };
        }

        private static PortableFormFieldDto ExportField(FormField field, FormStep step, IReadOnlyDictionary<int, PortableRefRow> refsById, List<string> notes)
        {
            var where = $"Step '{step.StepName}', field '{field.FieldName}'";
            if (field.IsLinkedToSource)
                notes.Add($"{where} is linked to a reusable field; exported as a copy.");
            if (field.DependsOnFieldId.HasValue)
                notes.Add($"{where} has a legacy DependsOnFieldId; not exported (use display conditions or validation rules).");

            return new PortableFormFieldDto
            {
                FieldGuid = field.FieldGuid.ToString(),
                FieldName = field.FieldName,
                Label = field.Label,
                Placeholder = field.Placeholder,
                Description = field.Description,
                FieldType = field.FieldType,
                ElementType = field.ElementType,
                ObjectType = field.ObjectType,
                EnumType = field.EnumType,
                DefaultValue = field.DefaultValue,
                IsRequired = field.Required,
                IsReadOnly = field.ReadOnly,
                MinSelection = field.MinSelection,
                MaxSelection = field.MaxSelection,
                CanCreate = field.CanCreate,
                IncrementValue = field.IncrementValue,
                DependencyConditionJson = field.DependencyConditionJson,
                SettingsJson = field.SettingsJson,
                SubConfigurationRef = ExportRef(field.SubConfigurationId, where, refsById, notes),
                Validations = field.Validations.Count == 0 ? null : field.Validations.OrderBy(v => v.Id).Select(v => new PortableFieldValidationDto
                {
                    ValidationType = v.Type,
                    ParametersJson = v.ParametersJson,
                    ErrorMessage = v.ErrorMessage,
                    IsActive = v.IsActive ? null : false
                }).ToList(),
                DisplayConditionGroups = ExportGroups(field.DisplayConditionGroups)
            };
        }

        private static PortableConfigurationRefDto? ExportRef(int? id, string where, IReadOnlyDictionary<int, PortableRefRow> refsById, List<string> notes)
        {
            if (!id.HasValue) return null;
            if (refsById.TryGetValue(id.Value, out var row))
            {
                return new PortableConfigurationRefDto
                {
                    ConfigurationGuid = row.ConfigurationGuid.ToString(),
                    EntityTypeName = row.EntityTypeName,
                    Name = row.Name
                };
            }

            notes.Add($"{where} refers to configuration {id} that no longer exists; the reference was left out.");
            return null;
        }

        private static List<PortableDisplayConditionGroupDto>? ExportGroups(List<DisplayConditionGroup> groups)
        {
            if (groups.Count == 0) return null;
            return groups.OrderBy(g => g.Order).Select(g => new PortableDisplayConditionGroupDto
            {
                InnerLogic = g.InnerLogic,
                CombineWithPreviousLogic = g.CombineWithPreviousLogic,
                IsActive = g.IsActive,
                Conditions = g.Conditions.OrderBy(c => c.Order).Select(c => new PortableDisplayConditionDto
                {
                    SourceFieldGuid = c.SourceFormFieldGuid.ToString(),
                    Operator = c.Operator,
                    ValueJson = c.ValueJson
                }).ToList()
            }).ToList();
        }

        // ---------------------------------------------------------------- import

        public async Task<FormConfigurationImportResultDto> ImportJsonAsync(string json, bool dryRun, bool makeDefault)
        {
            PortableFormConfigurationDto? file;
            try
            {
                file = JsonSerializer.Deserialize<PortableFormConfigurationDto>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                return Invalid(dryRun, $"Not a readable {PortableFormConfigurationDto.CurrentFormat} file: {ex.Message}");
            }

            if (file == null) return Invalid(dryRun, "The file is empty.");
            return await ImportAsync(file, dryRun, makeDefault);
        }

        public async Task<FormConfigurationImportResultDto> ImportAsync(PortableFormConfigurationDto file, bool dryRun, bool makeDefault)
        {
            if (file == null) return Invalid(dryRun, "The file is empty.");

            var result = new FormConfigurationImportResultDto
            {
                DryRun = dryRun,
                ConfigurationGuid = file.ConfigurationGuid,
                EntityTypeName = file.EntityTypeName,
                Name = file.Name
            };

            // 1. Structure.
            if (file.Format != PortableFormConfigurationDto.CurrentFormat)
                result.Errors.Add($"Unknown format '{file.Format ?? "(missing)"}': expected '{PortableFormConfigurationDto.CurrentFormat}'.");
            if (!Guid.TryParse(file.ConfigurationGuid, out var configGuid) || configGuid == Guid.Empty)
                result.Errors.Add("configurationGuid is missing or not a GUID; it is how an import tells whether the file was imported already.");
            if (string.IsNullOrWhiteSpace(file.EntityTypeName))
                result.Errors.Add("entityTypeName is required.");
            if (string.IsNullOrWhiteSpace(file.Name))
                result.Errors.Add("name is required.");
            if (file.Steps == null || file.Steps.Count == 0)
                result.Errors.Add("At least one step is required.");
            if (result.Errors.Count > 0) return Finish(result);

            // 2. Never overwrite.
            var existing = await _db.FormConfigurations.AsNoTracking()
                .Where(c => c.ConfigurationGuid == configGuid)
                .Select(c => new { c.Id, c.Name, c.IsDefault })
                .FirstOrDefaultAsync();
            if (existing != null)
            {
                result.Status = FormConfigurationImportStatus.AlreadyImported;
                result.FormConfigurationId = existing.Id.ToString();
                result.IsDefault = existing.IsDefault;
                result.Message = $"Already imported as configuration {existing.Id} ('{existing.Name}'). Nothing was written. " +
                                 "To import a changed copy, give the file a new configurationGuid and name.";
                return result;
            }

            // 3. Build the configuration and resolve every reference.
            var build = new ImportBuild(configGuid, result.Errors, result.Warnings);
            var entity = new FormConfiguration
            {
                ConfigurationGuid = configGuid,
                Name = file.Name!.Trim(),
                EntityTypeName = file.EntityTypeName!.Trim(),
                Description = BlankToNull(file.Description),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            for (int i = 0; i < file.Steps!.Count; i++)
            {
                entity.Steps.Add(await BuildStepAsync(file.Steps[i], $"Step {i + 1}", build, isChild: false));
            }
            entity.StepOrderJson = JsonSerializer.Serialize(entity.Steps.Select(s => s.StepGuid).ToArray());

            foreach (var (sourceGuid, where) in build.ConditionSources)
            {
                if (!build.TopLevelFieldsByGuid.ContainsKey(sourceGuid))
                    result.Errors.Add($"{where}: display condition reads field {sourceGuid}, which is not a field of this file.");
            }

            var rules = await BuildRulesAsync(file.ValidationRules ?? new(), build);

            // 4. Metadata drift (fields that no longer exist, wrong types, required properties, condition order).
            //    Runs even when references failed, so a dry run reports drift and unresolved references together.
            try
            {
                var validation = await _validationService.ValidateConfigurationAsync(entity, _metadataService);
                if (!validation.IsValid)
                    result.Errors.Add($"Metadata check: {validation.Summary}");
            }
            catch (Exception ex) when (result.Errors.Count > 0)
            {
                // A broken file (e.g. duplicate GUIDs) can trip the validator; the errors above already say why.
                result.Warnings.Add($"Metadata check skipped: {ex.Message}");
            }

            // 5. Default handling.
            var currentDefaults = await _db.FormConfigurations
                .Where(c => c.EntityTypeName == entity.EntityTypeName && c.IsDefault)
                .ToListAsync();
            result.IsDefault = currentDefaults.Count == 0 || makeDefault;
            if (makeDefault && currentDefaults.Count > 0)
            {
                result.ReplacedDefaultId = currentDefaults[0].Id.ToString();
                result.ReplacedDefaultName = currentDefaults[0].Name;
            }

            if (await _db.FormConfigurations.AnyAsync(c => c.EntityTypeName == entity.EntityTypeName && c.Name == entity.Name))
                result.Warnings.Add($"Another {entity.EntityTypeName} configuration is already called '{entity.Name}'.");

            result.StepCount = entity.Steps.Count;
            result.FieldCount = build.AllFieldsByGuid.Count;
            result.ValidationRuleCount = rules.Count;

            if (result.Errors.Count > 0 || dryRun) return Finish(result, currentDefaults.FirstOrDefault());

            // 6. Write: default swap, configuration, then rules (they need the field ids) in one transaction.
            await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;

            entity.IsDefault = result.IsDefault;
            if (makeDefault && currentDefaults.Count > 0)
            {
                foreach (var previous in currentDefaults)
                {
                    previous.IsDefault = false;
                    previous.UpdatedAt = DateTime.UtcNow;
                }
                await _db.SaveChangesAsync();
            }

            await _repo.AddAsync(entity);

            foreach (var rule in rules)
            {
                _db.FieldValidationRules.Add(new FieldValidationRule
                {
                    FormFieldId = rule.Field.Id,
                    DependsOnFieldId = rule.LocalDependsOn?.Id ?? rule.ExternalDependsOnId,
                    ValidationType = rule.Source.ValidationType!.Trim(),
                    DependencyPath = BlankToNull(rule.Source.DependencyPath),
                    ConfigJson = string.IsNullOrWhiteSpace(rule.Source.ConfigJson) ? "{}" : rule.Source.ConfigJson!,
                    ErrorMessage = rule.Source.ErrorMessage ?? string.Empty,
                    SuccessMessage = BlankToNull(rule.Source.SuccessMessage),
                    IsBlocking = rule.Source.IsBlocking,
                    RequiresDependencyFilled = rule.Source.RequiresDependencyFilled,
                    CreatedAt = DateTime.UtcNow
                });
            }
            if (rules.Count > 0) await _db.SaveChangesAsync();

            if (transaction != null) await transaction.CommitAsync();

            result.Status = FormConfigurationImportStatus.Imported;
            result.FormConfigurationId = entity.Id.ToString();
            result.Message = $"Imported as configuration {entity.Id}" +
                             (result.IsDefault ? " (default" + (result.ReplacedDefaultId != null ? $", replacing '{result.ReplacedDefaultName}'" : string.Empty) + ")." : " (not the default).");
            return result;
        }

        private sealed class ImportBuild
        {
            public ImportBuild(Guid configurationGuid, List<string> errors, List<string> warnings)
            {
                ConfigurationGuid = configurationGuid;
                Errors = errors;
                Warnings = warnings;
            }

            public Guid ConfigurationGuid { get; }
            public List<string> Errors { get; }
            public List<string> Warnings { get; }
            public HashSet<Guid> StepGuids { get; } = new();
            public Dictionary<Guid, FormField> AllFieldsByGuid { get; } = new();
            public Dictionary<Guid, FormField> TopLevelFieldsByGuid { get; } = new();
            public List<(Guid Source, string Where)> ConditionSources { get; } = new();
        }

        private sealed record PendingRule(PortableValidationRuleDto Source, FormField Field, FormField? LocalDependsOn, int? ExternalDependsOnId);

        private async Task<FormStep> BuildStepAsync(PortableFormStepDto source, string position, ImportBuild build, bool isChild)
        {
            var name = BlankToNull(source.StepName);
            var where = name != null ? $"Step '{name}'" : position;
            if (name == null) build.Errors.Add($"{position}: stepName is required.");

            var stepGuid = ReadGuid(source.StepGuid, $"{where} stepGuid", build);
            if (stepGuid != Guid.Empty && !build.StepGuids.Add(stepGuid))
                build.Errors.Add($"{where}: stepGuid {stepGuid} is used twice in the file.");

            var step = new FormStep
            {
                StepGuid = stepGuid == Guid.Empty ? Guid.NewGuid() : stepGuid,
                StepName = name ?? string.Empty,
                Description = BlankToNull(source.Description),
                IsManyToManyRelationship = source.IsManyToManyRelationship,
                RelatedEntityPropertyName = BlankToNull(source.RelatedEntityPropertyName),
                JoinEntityType = BlankToNull(source.JoinEntityType),
                CreatedAt = DateTime.UtcNow
            };
            step.SubConfigurationId = await ResolveRefAsync(source.SubConfigurationRef, where, build);
            if (step.IsManyToManyRelationship && source.SubConfigurationRef == null)
                build.Warnings.Add($"{where} is a many-to-many step without a subConfigurationRef (the join entry form).");

            foreach (var condition in source.Conditions ?? new())
            {
                var type = condition.ConditionType ?? "Entry";
                if (type != "Entry" && type != "Completion")
                    build.Errors.Add($"{where}: step condition type '{type}' is not 'Entry' or 'Completion'.");
                step.StepConditions.Add(new StepCondition
                {
                    ConditionType = type == "Completion" ? "CompletionCondition" : "StartCondition",
                    ConditionLogicJson = string.IsNullOrWhiteSpace(condition.ConditionJson) ? "{}" : condition.ConditionJson!,
                    ErrorMessage = BlankToNull(condition.ErrorMessage),
                    IsActive = condition.IsActive ?? true
                });
            }

            var fields = source.Fields ?? new();
            for (int j = 0; j < fields.Count; j++)
            {
                step.Fields.Add(await BuildFieldAsync(fields[j], where, j, build, isChild));
            }
            step.FieldOrderJson = JsonSerializer.Serialize(step.Fields.Select(f => f.FieldGuid).ToArray());

            step.DisplayConditionGroups = BuildGroups(source.DisplayConditionGroups, DisplayConditionTargetType.FormStep, where, build, isChild);

            var children = source.ChildFormSteps ?? new();
            for (int c = 0; c < children.Count; c++)
            {
                step.ChildFormSteps.Add(await BuildStepAsync(children[c], $"{where}, child step {c + 1}", build, isChild: true));
            }

            return step;
        }

        private async Task<FormField> BuildFieldAsync(PortableFormFieldDto source, string stepWhere, int index, ImportBuild build, bool isChild)
        {
            var fieldName = BlankToNull(source.FieldName);
            var where = $"{stepWhere}, field '{fieldName ?? $"#{index + 1}"}'";
            if (fieldName == null) build.Errors.Add($"{where}: fieldName is required.");
            if (string.IsNullOrWhiteSpace(source.Label)) build.Errors.Add($"{where}: label is required.");

            var fieldGuid = ReadGuid(source.FieldGuid, $"{where} fieldGuid", build);
            if (fieldGuid == Guid.Empty) fieldGuid = Guid.NewGuid();
            else if (build.AllFieldsByGuid.ContainsKey(fieldGuid))
                build.Errors.Add($"{where}: fieldGuid {fieldGuid} is used twice in the file.");

            CheckSettings(source.SettingsJson, where, build);

            var field = new FormField
            {
                FieldGuid = fieldGuid,
                FieldName = fieldName ?? string.Empty,
                Label = source.Label?.Trim() ?? string.Empty,
                Placeholder = BlankToNull(source.Placeholder),
                Description = BlankToNull(source.Description),
                FieldType = source.FieldType,
                ElementType = source.ElementType,
                ObjectType = BlankToNull(source.ObjectType),
                EnumType = BlankToNull(source.EnumType),
                DefaultValue = source.DefaultValue,
                Required = source.IsRequired,
                ReadOnly = source.IsReadOnly,
                MinSelection = source.MinSelection,
                MaxSelection = source.MaxSelection,
                CanCreate = source.CanCreate,
                IncrementValue = source.IncrementValue,
                DependencyConditionJson = BlankToNull(source.DependencyConditionJson),
                SettingsJson = BlankToNull(source.SettingsJson),
                CreatedAt = DateTime.UtcNow
            };
            field.SubConfigurationId = await ResolveRefAsync(source.SubConfigurationRef, where, build);

            foreach (var validation in source.Validations ?? new())
            {
                field.Validations.Add(new FieldValidation
                {
                    Type = validation.ValidationType,
                    ParametersJson = string.IsNullOrWhiteSpace(validation.ParametersJson) ? "{}" : validation.ParametersJson!,
                    ErrorMessage = BlankToNull(validation.ErrorMessage),
                    IsActive = validation.IsActive ?? true
                });
            }

            field.DisplayConditionGroups = BuildGroups(source.DisplayConditionGroups, DisplayConditionTargetType.FormField, where, build, isChild);

            build.AllFieldsByGuid[fieldGuid] = field;
            if (!isChild) build.TopLevelFieldsByGuid[fieldGuid] = field;
            return field;
        }

        private static List<DisplayConditionGroup> BuildGroups(
            List<PortableDisplayConditionGroupDto>? groups,
            DisplayConditionTargetType targetType,
            string where,
            ImportBuild build,
            bool isChild)
        {
            var built = new List<DisplayConditionGroup>();
            if (groups == null || groups.Count == 0) return built;

            if (isChild)
            {
                build.Errors.Add($"{where}: display conditions inside child steps can't be imported.");
                return built;
            }

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                var conditions = new List<DisplayCondition>();
                var sourceConditions = group.Conditions ?? new();
                for (int c = 0; c < sourceConditions.Count; c++)
                {
                    var condition = sourceConditions[c];
                    if (!Guid.TryParse(condition.SourceFieldGuid, out var sourceGuid) || sourceGuid == Guid.Empty)
                    {
                        build.Errors.Add($"{where}: display condition {c + 1} has no valid sourceFieldGuid.");
                        continue;
                    }
                    build.ConditionSources.Add((sourceGuid, where));
                    conditions.Add(new DisplayCondition
                    {
                        SourceFormFieldGuid = sourceGuid,
                        Operator = condition.Operator,
                        ValueJson = string.IsNullOrWhiteSpace(condition.ValueJson) ? "null" : condition.ValueJson!,
                        Order = c
                    });
                }

                built.Add(new DisplayConditionGroup
                {
                    TargetType = targetType,
                    InnerLogic = group.InnerLogic,
                    CombineWithPreviousLogic = group.CombineWithPreviousLogic,
                    IsActive = group.IsActive,
                    Order = g,
                    Conditions = conditions
                });
            }

            return built;
        }

        private async Task<List<PendingRule>> BuildRulesAsync(List<PortableValidationRuleDto> sources, ImportBuild build)
        {
            var pending = new List<PendingRule>();
            for (int i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                var where = $"Validation rule {i + 1}";

                if (!Guid.TryParse(source.FieldGuid, out var fieldGuid) || !build.AllFieldsByGuid.TryGetValue(fieldGuid, out var field))
                {
                    build.Errors.Add($"{where}: fieldGuid '{source.FieldGuid}' is not a field of this file.");
                    continue;
                }
                where = $"{where} ({field.FieldName})";
                if (string.IsNullOrWhiteSpace(source.ValidationType))
                {
                    build.Errors.Add($"{where}: validationType is required.");
                    continue;
                }

                FormField? localDependsOn = null;
                int? externalDependsOnId = null;
                if (source.DependsOn != null)
                {
                    if (!Guid.TryParse(source.DependsOn.FieldGuid, out var dependsOnGuid))
                    {
                        build.Errors.Add($"{where}: dependsOn.fieldGuid '{source.DependsOn.FieldGuid}' is not a GUID.");
                        continue;
                    }

                    var isLocal = string.IsNullOrWhiteSpace(source.DependsOn.ConfigurationGuid)
                                  || (Guid.TryParse(source.DependsOn.ConfigurationGuid, out var ownGuid) && ownGuid == build.ConfigurationGuid);
                    if (isLocal)
                    {
                        if (!build.AllFieldsByGuid.TryGetValue(dependsOnGuid, out localDependsOn))
                        {
                            build.Errors.Add($"{where}: dependsOn field {dependsOnGuid} is not a field of this file.");
                            continue;
                        }
                        if (localDependsOn == field)
                        {
                            build.Errors.Add($"{where}: a rule can't depend on its own field.");
                            continue;
                        }
                    }
                    else if (!Guid.TryParse(source.DependsOn.ConfigurationGuid, out var otherGuid))
                    {
                        build.Errors.Add($"{where}: dependsOn.configurationGuid '{source.DependsOn.ConfigurationGuid}' is not a GUID.");
                        continue;
                    }
                    else
                    {
                        var id = await _db.FormFields.AsNoTracking()
                            .Where(f => f.FieldGuid == dependsOnGuid
                                        && f.FormStep != null
                                        && f.FormStep.FormConfiguration != null
                                        && f.FormStep.FormConfiguration.ConfigurationGuid == otherGuid)
                            .Select(f => (int?)f.Id)
                            .FirstOrDefaultAsync();
                        if (id == null)
                        {
                            build.Errors.Add($"{where}: dependsOn field {dependsOnGuid} of configuration {otherGuid} is not in this database. " +
                                             $"Import that configuration first.{await SeedHintAsync(otherGuid)}");
                            continue;
                        }
                        externalDependsOnId = id;
                    }
                }

                pending.Add(new PendingRule(source, field, localDependsOn, externalDependsOnId));
            }
            return pending;
        }

        private async Task<int?> ResolveRefAsync(PortableConfigurationRefDto? reference, string where, ImportBuild build)
        {
            if (reference == null) return null;

            var described = DescribeRef(reference);
            if (!string.IsNullOrWhiteSpace(reference.ConfigurationGuid))
            {
                if (!Guid.TryParse(reference.ConfigurationGuid, out var guid))
                {
                    build.Errors.Add($"{where}: subConfigurationRef.configurationGuid '{reference.ConfigurationGuid}' is not a GUID.");
                    return null;
                }
                if (guid == build.ConfigurationGuid)
                {
                    build.Errors.Add($"{where}: subConfigurationRef points at this configuration itself.");
                    return null;
                }

                var target = await _db.FormConfigurations.AsNoTracking()
                    .Where(c => c.ConfigurationGuid == guid)
                    .Select(c => new { c.Id, c.EntityTypeName })
                    .FirstOrDefaultAsync();
                if (target == null)
                {
                    build.Errors.Add($"{where} refers to configuration {described}, which is not in this database. Import it first.{await SeedHintAsync(guid)}");
                    return null;
                }
                if (!string.IsNullOrWhiteSpace(reference.EntityTypeName)
                    && !string.Equals(reference.EntityTypeName, target.EntityTypeName, StringComparison.OrdinalIgnoreCase))
                {
                    build.Warnings.Add($"{where}: subConfigurationRef says entity '{reference.EntityTypeName}' but configuration {target.Id} is for '{target.EntityTypeName}'.");
                }
                return target.Id;
            }

            if (!string.IsNullOrWhiteSpace(reference.EntityTypeName))
            {
                var fallback = await _db.FormConfigurations.AsNoTracking()
                    .Where(c => c.EntityTypeName == reference.EntityTypeName && c.IsDefault)
                    .Select(c => new { c.Id, c.Name })
                    .FirstOrDefaultAsync();
                if (fallback == null)
                {
                    build.Errors.Add($"{where} refers to the default {reference.EntityTypeName} configuration, but there is none in this database.");
                    return null;
                }
                build.Warnings.Add($"{where}: subConfigurationRef has no configurationGuid; resolved by entity to the default '{fallback.Name}' (id {fallback.Id}).");
                return fallback.Id;
            }

            build.Errors.Add($"{where}: subConfigurationRef needs a configurationGuid or an entityTypeName.");
            return null;
        }

        private static void CheckSettings(string? settingsJson, string where, ImportBuild build)
        {
            if (string.IsNullOrWhiteSpace(settingsJson)) return;
            try
            {
                using var doc = JsonDocument.Parse(settingsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    build.Errors.Add($"{where}: settingsJson must be a JSON object.");
                    return;
                }
                if (doc.RootElement.TryGetProperty("worldTask", out var worldTask)
                    && worldTask.ValueKind == JsonValueKind.Object
                    && worldTask.TryGetProperty("taskType", out var taskType)
                    && taskType.ValueKind == JsonValueKind.String
                    && !KnownWorldTaskTypes.Contains(taskType.GetString()!))
                {
                    build.Warnings.Add($"{where}: world task type '{taskType.GetString()}' is not one of the known types ({string.Join(", ", KnownWorldTaskTypes)}).");
                }
            }
            catch (JsonException ex)
            {
                build.Errors.Add($"{where}: settingsJson is not valid JSON ({ex.Message}).");
            }
        }

        private static Guid ReadGuid(string? value, string what, ImportBuild build)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                build.Warnings.Add($"{what} is missing; a new GUID was generated (nothing else can refer to it).");
                return Guid.Empty;
            }
            if (!Guid.TryParse(value, out var guid) || guid == Guid.Empty)
            {
                build.Errors.Add($"{what} '{value}' is not a GUID.");
                return Guid.Empty;
            }
            return guid;
        }

        private static string DescribeRef(PortableConfigurationRefDto reference)
        {
            var label = reference.Name ?? reference.EntityTypeName;
            return label != null ? $"'{label}' ({reference.ConfigurationGuid})" : $"{reference.ConfigurationGuid}";
        }

        private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static FormConfigurationImportResultDto Invalid(bool dryRun, string error)
        {
            var result = new FormConfigurationImportResultDto { DryRun = dryRun };
            result.Errors.Add(error);
            return Finish(result);
        }

        private static FormConfigurationImportResultDto Finish(FormConfigurationImportResultDto result, FormConfiguration? currentDefault = null)
        {
            if (result.Errors.Count > 0)
            {
                result.Status = FormConfigurationImportStatus.Invalid;
                result.Message = $"{result.Errors.Count} problem(s) found; nothing was written. {result.Errors[0]}";
                return result;
            }

            result.Status = FormConfigurationImportStatus.DryRunPassed;
            result.Message = "Dry run passed: nothing was written. An import would create it as " +
                             (result.IsDefault
                                 ? (result.ReplacedDefaultId != null ? $"the default, replacing '{result.ReplacedDefaultName}'." : "the default (the entity has none).")
                                 : $"a non-default configuration ('{currentDefault?.Name}' stays the default).");
            return result;
        }

        // ---------------------------------------------------------------- bundled seeds

        public async Task<IReadOnlyList<FormConfigurationSeedDto>> ListSeedsAsync()
        {
            var seeds = new List<FormConfigurationSeedDto>();
            var guids = new Dictionary<Guid, FormConfigurationSeedDto>();

            foreach (var path in SeedFiles())
            {
                var seed = new FormConfigurationSeedDto { Name = Path.GetFileNameWithoutExtension(path) };
                seeds.Add(seed);
                try
                {
                    var file = JsonSerializer.Deserialize<PortableFormConfigurationDto>(await File.ReadAllTextAsync(path), JsonOptions);
                    if (file == null) { seed.Error = "The file is empty."; continue; }

                    seed.ConfigurationGuid = file.ConfigurationGuid;
                    seed.EntityTypeName = file.EntityTypeName;
                    seed.ConfigurationName = file.Name;
                    seed.Description = file.Description;
                    seed.StepCount = file.Steps?.Count ?? 0;
                    seed.FieldCount = file.Steps?.Sum(s => s.Fields?.Count ?? 0) ?? 0;
                    if (file.Format != PortableFormConfigurationDto.CurrentFormat)
                        seed.Error = $"Unknown format '{file.Format ?? "(missing)"}'.";
                    else if (Guid.TryParse(file.ConfigurationGuid, out var guid))
                        guids[guid] = seed;
                    else
                        seed.Error = "configurationGuid is missing or not a GUID.";
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    seed.Error = $"Not a readable {PortableFormConfigurationDto.CurrentFormat} file: {ex.Message}";
                }
            }

            if (guids.Count > 0)
            {
                var keys = guids.Keys.ToList();
                var imported = await _db.FormConfigurations.AsNoTracking()
                    .Where(c => keys.Contains(c.ConfigurationGuid))
                    .Select(c => new { c.Id, c.ConfigurationGuid })
                    .ToListAsync();
                foreach (var row in imported)
                {
                    guids[row.ConfigurationGuid].Imported = true;
                    guids[row.ConfigurationGuid].ImportedFormConfigurationId = row.Id.ToString();
                }
            }

            return seeds;
        }

        public async Task<FormConfigurationImportResultDto?> ImportSeedAsync(string name, bool dryRun, bool makeDefault)
        {
            var path = SeedPath(name);
            if (path == null) return null;
            return await ImportJsonAsync(await File.ReadAllTextAsync(path), dryRun, makeDefault);
        }

        private string? SeedPath(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !SeedNamePattern.IsMatch(name)) return null;
            var path = Path.Combine(_seedOptions.Directory, name + ".json");
            return File.Exists(path) ? path : null;
        }

        private IEnumerable<string> SeedFiles()
        {
            if (string.IsNullOrWhiteSpace(_seedOptions.Directory) || !System.IO.Directory.Exists(_seedOptions.Directory))
                return Array.Empty<string>();
            return System.IO.Directory.GetFiles(_seedOptions.Directory, "*.json")
                .Where(p => SeedNamePattern.IsMatch(Path.GetFileNameWithoutExtension(p)))
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>" It is the bundled seed 'X'." when a seed file carries that configurationGuid.</summary>
        private async Task<string> SeedHintAsync(Guid configurationGuid)
        {
            if (_seedNamesByGuid == null)
            {
                _seedNamesByGuid = new Dictionary<Guid, string>();
                foreach (var path in SeedFiles())
                {
                    try
                    {
                        var file = JsonSerializer.Deserialize<PortableFormConfigurationDto>(await File.ReadAllTextAsync(path), JsonOptions);
                        if (file != null && Guid.TryParse(file.ConfigurationGuid, out var guid))
                            _seedNamesByGuid.TryAdd(guid, Path.GetFileNameWithoutExtension(path));
                    }
                    catch (Exception ex) when (ex is JsonException or IOException)
                    {
                        // Listed with its error by ListSeedsAsync; no hint from it.
                    }
                }
            }

            return _seedNamesByGuid.TryGetValue(configurationGuid, out var name) ? $" It is the bundled seed '{name}'." : string.Empty;
        }
    }
}
