using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>Where the bundled display seed files live (Data/DisplayConfigurationSeeds/ under the content root).</summary>
    public sealed class DisplayConfigurationSeedOptions
    {
        public string Directory { get; init; } = string.Empty;
    }

    /// <summary>
    /// "knk-displayconfig/1" (spec: Data/DisplayConfigurationSeeds/README.md). Export writes a display configuration
    /// as a portable file; import builds a new configuration from one. Import rules (README §3): never overwrite (an
    /// existing configurationGuid writes nothing), default only when the entity has none or makeDefault is asked,
    /// published unless asDraft is asked, and every structure, element and metadata check runs before anything is
    /// written. Metadata checks use MetadataService (the model); elements marked metadataCheck "warn" follow the read
    /// DTO on purpose, so their metadata mismatches are warnings that end in <see cref="WarnMarker"/>.
    /// </summary>
    public class DisplayConfigurationPortabilityService : IDisplayConfigurationPortabilityService
    {
        /// <summary>Ends every warning that would have been an error without metadataCheck "warn".</summary>
        public const string WarnMarker = " (metadataCheck \"warn\")";

        /// <summary>The ActionButtonsConfigDto keys the renderer reads (case-sensitive JSON names).</summary>
        private static readonly IReadOnlySet<string> KnownButtonKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "showViewButton", "showEditButton", "showCreateButton", "showSelectButton",
            "showUnlinkButton", "showAddButton", "showRemoveButton"
        };

        /// <summary>Relationship buttons the renderer hides while KNG-148 is open (RELATIONSHIP_ACTIONS_AVAILABLE = false).</summary>
        private static readonly IReadOnlySet<string> HiddenButtonKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "showSelectButton", "showUnlinkButton", "showAddButton", "showRemoveButton"
        };

        /// <summary>The fieldType values the renderer formats or the builder offers (README rule E5).</summary>
        private static readonly IReadOnlySet<string> KnownFieldTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "String", "Integer", "Number", "Boolean", "DateTime", "Date", "Decimal", "Enum", "Object", "List"
        };

        /// <summary>The fieldTypes DisplayField.tsx offers a hot-edit pencil for.</summary>
        private static readonly IReadOnlySet<string> EditableFieldTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "String", "Integer", "Number", "Boolean"
        };

        private static readonly Regex SeedNamePattern = new("^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.Compiled);
        private static readonly Regex TemplatePathPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.Compiled);
        private static readonly Regex TemplateNumberPattern = new(@"^\d+(\.\d+)?$", RegexOptions.Compiled);

        /// <summary>[FormConfigurableEntity] model types by name, for the get-only check of rule E6.</summary>
        private static readonly Lazy<Dictionary<string, Type>> ModelTypes = new(() =>
            typeof(DisplayConfiguration).Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<FormConfigurableEntityAttribute>() != null)
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase));

        private readonly KnKDbContext _db;
        private readonly IMetadataService _metadataService;
        private readonly DisplayConfigurationSeedOptions _seedOptions;

        public DisplayConfigurationPortabilityService(
            KnKDbContext db,
            IMetadataService metadataService,
            DisplayConfigurationSeedOptions seedOptions)
        {
            _db = db;
            _metadataService = metadataService;
            _seedOptions = seedOptions;
        }

        public static string Serialize(PortableDisplayConfigurationDto file) =>
            JsonSerializer.Serialize(file, FormConfigurationPortabilityService.JsonOptions);

        // ---------------------------------------------------------------- export

        public async Task<PortableDisplayConfigurationDto?> ExportAsync(int id)
        {
            var config = await _db.DisplayConfigurations.AsNoTracking()
                .Include(c => c.Sections).ThenInclude(s => s.Fields)
                .Include(c => c.Sections).ThenInclude(s => s.SubSections).ThenInclude(s => s.Fields)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (config == null) return null;

            var notes = new List<string>
            {
                $"Exported from display configuration {config.Id} ({(config.IsDraft ? "draft" : "published")}, " +
                $"{(config.IsDefault ? "default" : "not default")}); the import decides draft and default again."
            };

            var sections = config.Sections.Concat(config.Sections.SelectMany(s => s.SubSections)).ToList();

            // Linked sections/fields carry no content of their own (nothing resolves the link at read time); the
            // export writes their source's content instead.
            var sourceSectionIds = sections.Where(s => s.IsLinkedToSource && s.SourceSectionId.HasValue)
                .Select(s => s.SourceSectionId!.Value).Distinct().ToList();
            var sourceSections = await _db.DisplaySections.AsNoTracking()
                .Include(s => s.Fields)
                .Where(s => sourceSectionIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id);

            var fields = sections.SelectMany(s => s.Fields).Concat(sourceSections.Values.SelectMany(s => s.Fields));
            var sourceFieldIds = fields.Where(f => f.IsLinkedToSource && f.SourceFieldId.HasValue)
                .Select(f => f.SourceFieldId!.Value).Distinct().ToList();
            var sourceFields = await _db.DisplayFields.AsNoTracking()
                .Where(f => sourceFieldIds.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id);

            // The repository loads two levels; anything deeper is never rendered and not carried.
            var subSectionIds = config.Sections.SelectMany(s => s.SubSections).Select(s => s.Id).ToList();
            var deeper = await _db.DisplaySections.AsNoTracking()
                .Where(s => s.ParentSectionId != null && subSectionIds.Contains(s.ParentSectionId.Value))
                .Select(s => new { ParentId = s.ParentSectionId!.Value, s.SectionName })
                .ToListAsync();

            var state = new ExportState(notes, sourceSections, sourceFields,
                deeper.ToLookup(d => d.ParentId, d => d.SectionName));

            var file = new PortableDisplayConfigurationDto
            {
                Format = PortableDisplayConfigurationDto.CurrentFormat,
                ConfigurationGuid = config.ConfigurationGuid.ToString(),
                EntityTypeName = config.EntityTypeName,
                Name = config.Name,
                Description = BlankToNull(config.Description),
                Sections = OrderByGuidList(config.Sections, config.SectionOrderJson, s => s.SectionGuid, s => s.Id,
                        s => $"Section '{s.SectionName}'", "the configuration's section order", notes)
                    .Select(s => ExportSection(s, isSubSection: false, state))
                    .ToList()
            };

            file.Notes = notes;
            return file;
        }

        private sealed record ExportState(
            List<string> Notes,
            IReadOnlyDictionary<int, DisplaySection> SourceSections,
            IReadOnlyDictionary<int, DisplayField> SourceFields,
            ILookup<int, string> DeeperSectionNames)
        {
            public HashSet<Guid> SectionGuids { get; } = new();
            public HashSet<Guid> FieldGuids { get; } = new();
        }

        private static PortableDisplaySectionDto ExportSection(DisplaySection section, bool isSubSection, ExportState state)
        {
            var where = $"Section '{section.SectionName}'";
            var content = section;
            if (section.IsLinkedToSource)
            {
                if (section.SourceSectionId.HasValue && state.SourceSections.TryGetValue(section.SourceSectionId.Value, out var source))
                {
                    content = source;
                    state.Notes.Add($"{where} is linked to reusable section {source.Id}; exported with that section's name, settings and fields (imports are never linked).");
                }
                else
                {
                    state.Notes.Add($"{where} is linked to a reusable section that no longer exists; exported without fields.");
                }
            }
            else if (section.SourceSectionId.HasValue)
            {
                state.Notes.Add($"{where} was copied from reusable section {section.SourceSectionId}; exported as a plain copy.");
            }

            var sectionGuid = UniqueGuid(section.SectionGuid, state.SectionGuids, where, "sectionGuid", state.Notes);
            // A linked section keeps its own field order (copied from the source, and editable); its fields are the source's.
            var fieldOrderJson = string.IsNullOrWhiteSpace(section.FieldOrderJson) || section.FieldOrderJson.Trim() == "[]"
                ? content.FieldOrderJson
                : section.FieldOrderJson;

            var dto = new PortableDisplaySectionDto
            {
                SectionGuid = sectionGuid.ToString(),
                SectionName = content.SectionName,
                Description = BlankToNull(content.Description),
                RelatedEntityPropertyName = BlankToNull(content.RelatedEntityPropertyName),
                RelatedEntityTypeName = BlankToNull(content.RelatedEntityTypeName),
                IsCollection = content.IsCollection,
                IsReusable = section.IsReusable,
                ActionButtonsConfigJson = string.IsNullOrWhiteSpace(content.ActionButtonsConfigJson) || content.ActionButtonsConfigJson.Trim() == "{}"
                    ? null
                    : content.ActionButtonsConfigJson,
                Fields = OrderByGuidList(content.Fields, fieldOrderJson, f => f.FieldGuid, f => f.Id,
                        f => $"{where}, field '{f.Label}'", $"{where}'s field order", state.Notes)
                    .Select(f => ExportField(f, where, state))
                    .ToList()
            };

            if (!isSubSection && section.SubSections.Count > 0)
            {
                dto.SubSections = section.SubSections.OrderBy(s => s.Id).Select(s =>
                {
                    foreach (var name in state.DeeperSectionNames[s.Id])
                        state.Notes.Add($"{where}, sub-section '{s.SectionName}' has its own sub-section '{name}'; left out (only one level is loaded and rendered).");
                    return ExportSection(s, isSubSection: true, state);
                }).ToList();
            }

            return dto;
        }

        private static PortableDisplayFieldDto ExportField(DisplayField field, string sectionWhere, ExportState state)
        {
            var where = $"{sectionWhere}, field '{field.Label}'";
            var content = field;
            if (field.IsLinkedToSource)
            {
                if (field.SourceFieldId.HasValue && state.SourceFields.TryGetValue(field.SourceFieldId.Value, out var source))
                {
                    content = source;
                    state.Notes.Add($"{where} is linked to reusable field {source.Id}; exported with that field's content.");
                }
                else
                {
                    state.Notes.Add($"{where} is linked to a reusable field that no longer exists; exported with its label only.");
                }
            }
            else if (field.SourceFieldId.HasValue)
            {
                state.Notes.Add($"{where} was copied from reusable field {field.SourceFieldId}; exported as a plain copy.");
            }

            return new PortableDisplayFieldDto
            {
                FieldGuid = UniqueGuid(field.FieldGuid, state.FieldGuids, where, "fieldGuid", state.Notes).ToString(),
                FieldName = BlankToNull(content.FieldName),
                RelatedEntityPropertyName = BlankToNull(content.RelatedEntityPropertyName),
                RelatedEntityTypeName = BlankToNull(content.RelatedEntityTypeName),
                Label = content.Label,
                Description = BlankToNull(content.Description),
                TemplateText = BlankToNull(content.TemplateText),
                FieldType = BlankToNull(content.FieldType),
                IsEditableInDisplay = content.IsEditableInDisplay,
                IsReusable = field.IsReusable
            };
        }

        /// <summary>Two copies of one linked source would share GUIDs; the second gets a new one (nothing refers to it).</summary>
        private static Guid UniqueGuid(Guid guid, HashSet<Guid> used, string where, string what, List<string> notes)
        {
            if (guid != Guid.Empty && used.Add(guid)) return guid;
            var fresh = Guid.NewGuid();
            used.Add(fresh);
            notes.Add($"{where}: its {what} {guid} was already used in this file; exported with a new one.");
            return fresh;
        }

        /// <summary>
        /// The renderer's rule: a non-empty GUID order list shows exactly the listed items in list order, so unlisted
        /// items are invisible and left out; an empty or unreadable list shows everything (Id order here).
        /// </summary>
        private static List<T> OrderByGuidList<T>(
            IEnumerable<T> items,
            string? orderJson,
            Func<T, Guid> guidOf,
            Func<T, int> idOf,
            Func<T, string> describe,
            string listName,
            List<string> notes)
        {
            var all = items.OrderBy(idOf).ToList();
            List<Guid>? order = null;
            if (!string.IsNullOrWhiteSpace(orderJson))
            {
                try
                {
                    var raw = JsonSerializer.Deserialize<List<string>>(orderJson);
                    if (raw != null && raw.All(g => Guid.TryParse(g, out _)))
                        order = raw.Select(Guid.Parse).Distinct().ToList();
                    else
                        notes.Add($"{listName} is not a list of GUIDs; everything was exported in creation order.");
                }
                catch (JsonException)
                {
                    notes.Add($"{listName} is not valid JSON; everything was exported in creation order.");
                }
            }
            if (order == null || order.Count == 0) return all;

            var byGuid = all.GroupBy(guidOf).ToDictionary(g => g.Key, g => g.First());
            var ordered = new List<T>();
            foreach (var guid in order)
            {
                if (byGuid.TryGetValue(guid, out var item)) ordered.Add(item);
                else notes.Add($"{listName} lists {guid}, which does not exist; ignored.");
            }
            foreach (var item in all.Where(i => !ordered.Contains(i)))
                notes.Add($"{describe(item)} is not in {listName}, so it is never shown; left out.");
            return ordered;
        }

        // ---------------------------------------------------------------- import

        public async Task<DisplayConfigurationImportResultDto> ImportJsonAsync(string json, bool dryRun, bool makeDefault, bool asDraft)
        {
            PortableDisplayConfigurationDto? file;
            try
            {
                file = JsonSerializer.Deserialize<PortableDisplayConfigurationDto>(json, FormConfigurationPortabilityService.JsonOptions);
            }
            catch (JsonException ex)
            {
                return Invalid(dryRun, asDraft, $"Not a readable {PortableDisplayConfigurationDto.CurrentFormat} file: {ex.Message}");
            }

            if (file == null) return Invalid(dryRun, asDraft, "The file is empty.");
            return await ImportAsync(file, dryRun, makeDefault, asDraft);
        }

        public async Task<DisplayConfigurationImportResultDto> ImportAsync(PortableDisplayConfigurationDto file, bool dryRun, bool makeDefault, bool asDraft)
        {
            if (file == null) return Invalid(dryRun, asDraft, "The file is empty.");

            var result = new DisplayConfigurationImportResultDto
            {
                DryRun = dryRun,
                IsDraft = asDraft,
                ConfigurationGuid = file.ConfigurationGuid,
                EntityTypeName = file.EntityTypeName,
                Name = file.Name
            };

            // S1. Structure.
            if (file.Format != PortableDisplayConfigurationDto.CurrentFormat)
                result.Errors.Add($"Unknown format '{file.Format ?? "(missing)"}': expected '{PortableDisplayConfigurationDto.CurrentFormat}'.");
            if (!Guid.TryParse(file.ConfigurationGuid, out var configGuid) || configGuid == Guid.Empty)
                result.Errors.Add("configurationGuid is missing or not a GUID; it is how an import tells whether the file was imported already.");
            if (string.IsNullOrWhiteSpace(file.EntityTypeName))
                result.Errors.Add("entityTypeName is required.");
            if (string.IsNullOrWhiteSpace(file.Name))
                result.Errors.Add("name is required.");
            if (file.Sections == null || file.Sections.Count == 0)
                result.Errors.Add("At least one section is required.");
            if (result.Errors.Count > 0) return Finish(result);

            // I1. Never overwrite.
            var existing = await _db.DisplayConfigurations.AsNoTracking()
                .Where(c => c.ConfigurationGuid == configGuid)
                .Select(c => new { c.Id, c.Name, c.IsDefault, c.IsDraft })
                .FirstOrDefaultAsync();
            if (existing != null)
            {
                result.Status = FormConfigurationImportStatus.AlreadyImported;
                result.DisplayConfigurationId = existing.Id.ToString();
                result.IsDefault = existing.IsDefault;
                result.IsDraft = existing.IsDraft;
                result.Message = $"Already imported as display configuration {existing.Id} ('{existing.Name}'). Nothing was written. " +
                                 "To import a changed copy, give the file a new configurationGuid and name.";
                return result;
            }

            // Build the configuration; every element is checked on the way (S2-S5, E1-E6, M1-M6).
            var build = new ImportBuild(result.Errors, result.Warnings, _metadataService.GetAllEntityMetadata());
            var entityTypeName = file.EntityTypeName!.Trim();
            var rootMeta = build.Meta(entityTypeName);
            if (rootMeta == null)
                result.Errors.Add($"entityTypeName '{entityTypeName}' is not an entity the API has metadata for (a [FormConfigurableEntity] model).");

            var entity = new DisplayConfiguration
            {
                ConfigurationGuid = configGuid,
                Name = file.Name!.Trim(),
                // The metadata's spelling, so the default lookups (EntityTypeName ==) match whatever casing the file used.
                EntityTypeName = rootMeta?.EntityName ?? entityTypeName,
                Description = BlankToNull(file.Description),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            CheckLength(entity.Name, 200, "name", build);
            CheckLength(entity.EntityTypeName, 100, "entityTypeName", build);
            CheckLength(entity.Description, 500, "description", build);

            var root = rootMeta?.EntityName;
            for (int i = 0; i < file.Sections!.Count; i++)
            {
                entity.Sections.Add(BuildSection(file.Sections[i], $"Section {i + 1}", build, parent: null, root));
            }
            entity.SectionOrderJson = JsonSerializer.Serialize(entity.Sections.Select(s => s.SectionGuid).ToArray());

            // I2. Default handling.
            var currentDefaults = await _db.DisplayConfigurations
                .Where(c => c.EntityTypeName == entity.EntityTypeName && c.IsDefault)
                .ToListAsync();
            result.IsDefault = currentDefaults.Count == 0 || makeDefault;
            if (makeDefault && currentDefaults.Count > 0)
            {
                result.ReplacedDefaultId = currentDefaults[0].Id.ToString();
                result.ReplacedDefaultName = currentDefaults[0].Name;
            }

            if (await _db.DisplayConfigurations.AnyAsync(c => c.EntityTypeName == entity.EntityTypeName && c.Name == entity.Name))
                result.Warnings.Add($"Another {entity.EntityTypeName} display configuration is already called '{entity.Name}'.");

            result.SectionCount = build.SectionGuids.Count;
            result.FieldCount = build.FieldGuids.Count;

            if (result.Errors.Count > 0 || dryRun) return Finish(result, currentDefaults.FirstOrDefault());

            // Write through the context (DisplayConfigurationService.CreateAsync forces a draft and refuses a second
            // default): default swap and the configuration graph in one transaction.
            await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;

            entity.IsDefault = result.IsDefault;
            entity.IsDraft = asDraft;
            if (makeDefault)
            {
                foreach (var previous in currentDefaults)
                {
                    previous.IsDefault = false;
                    previous.UpdatedAt = DateTime.UtcNow;
                }
            }

            _db.DisplayConfigurations.Add(entity);
            await _db.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();

            result.Status = FormConfigurationImportStatus.Imported;
            result.DisplayConfigurationId = entity.Id.ToString();
            result.Message = $"Imported as display configuration {entity.Id}, {(asDraft ? "as a draft" : "published")}" +
                             (result.IsDefault
                                 ? " and the default" + (result.ReplacedDefaultId != null ? $", replacing '{result.ReplacedDefaultName}'." : ".")
                                 : " (not the default).");
            return result;
        }

        private sealed class ImportBuild
        {
            private readonly Dictionary<string, EntityMetadataDto> _metadata;

            public ImportBuild(List<string> errors, List<string> warnings, IEnumerable<EntityMetadataDto> metadata)
            {
                Errors = errors;
                Warnings = warnings;
                _metadata = metadata
                    .GroupBy(m => m.EntityName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }

            public List<string> Errors { get; }
            public List<string> Warnings { get; }
            public HashSet<Guid> SectionGuids { get; } = new();
            public HashSet<Guid> FieldGuids { get; } = new();

            public EntityMetadataDto? Meta(string? entityName) =>
                entityName != null && _metadata.TryGetValue(entityName, out var meta) ? meta : null;

            /// <summary>A metadata mismatch: an error, or a marked warning under metadataCheck "warn".</summary>
            public void Mismatch(bool warn, string message)
            {
                if (warn) Warnings.Add(message + WarnMarker);
                else Errors.Add(message);
            }

            /// <summary>Something the importer could not check, or a hint: always a warning (marked under "warn").</summary>
            public void Note(bool warn, string message) => Warnings.Add(warn ? message + WarnMarker : message);
        }

        /// <param name="root">The configuration's entity (canonical metadata name), or null when it has no metadata.</param>
        private DisplaySection BuildSection(PortableDisplaySectionDto source, string position, ImportBuild build, DisplaySection? parent, string? root)
        {
            var name = BlankToNull(source.SectionName);
            var where = parent == null
                ? (name != null ? $"Section '{name}'" : position)
                : $"Section '{parent.SectionName}', sub-section '{name ?? position}'";
            if (name == null) build.Errors.Add($"{where}: sectionName is required.");
            CheckLength(name, 200, $"{where}: sectionName", build);
            CheckLength(source.Description, 500, $"{where}: description", build);

            var sectionGuid = ReadGuid(source.SectionGuid, $"{where} sectionGuid", build);
            if (sectionGuid == Guid.Empty) sectionGuid = Guid.NewGuid();
            else if (build.SectionGuids.Contains(sectionGuid))
                build.Errors.Add($"{where}: sectionGuid {sectionGuid} is used twice in the file.");
            build.SectionGuids.Add(sectionGuid);

            var rel = BlankToNull(source.RelatedEntityPropertyName);
            var relType = BlankToNull(source.RelatedEntityTypeName);
            CheckLength(rel, 100, $"{where}: relatedEntityPropertyName", build);
            CheckLength(relType, 100, $"{where}: relatedEntityTypeName", build);
            CheckRelatedPair(rel, relType, where, build);
            var warn = ReadMetadataCheck(source.MetadataCheck, where, build);
            if (source.DisplayConditionGroups is { Count: > 0 })
                build.Errors.Add($"{where}: display conditions are not supported by the model yet; displayConditionGroups must be empty.");
            if (parent == null && source.IsCollection && rel == null)
                build.Warnings.Add($"{where} is a collection without relatedEntityPropertyName; nothing is listed (the section reads the entity itself, not a list).");

            var section = new DisplaySection
            {
                SectionGuid = sectionGuid,
                SectionName = name ?? string.Empty,
                Description = BlankToNull(source.Description),
                RelatedEntityPropertyName = rel,
                RelatedEntityTypeName = relType,
                IsCollection = source.IsCollection,
                IsReusable = source.IsReusable,
                ActionButtonsConfigJson = CheckButtons(source.ActionButtonsConfigJson, relType, where, build),
                CreatedAt = DateTime.UtcNow
            };

            // Where this section's fields read from (README §1.1): fields without their own relatedEntityPropertyName
            // read the section's data; fields with one read from the root entity in a single section and from the
            // item in a collection. A sub-section is a collection's item template: everything reads the item.
            string? dataType;
            string? relBase;
            if (parent != null)
            {
                if (rel != null || relType != null)
                    build.Warnings.Add($"{where}: relatedEntityPropertyName/TypeName of a sub-section are ignored; its fields read each item of the collection.");
                dataType = relBase = parent.RelatedEntityTypeName;
            }
            else if (rel == null)
            {
                dataType = relBase = root;
            }
            else
            {
                CheckRelatedProperty(root, rel, relType, source.IsCollection, where, warn, build);
                dataType = relType;
                relBase = source.IsCollection ? relType : root;
            }

            var editableHere = parent == null && rel == null && !source.IsCollection;
            var fields = source.Fields ?? new();
            for (int j = 0; j < fields.Count; j++)
            {
                section.Fields.Add(BuildField(fields[j], where, j, build, dataType, relBase, editableHere));
            }
            section.FieldOrderJson = JsonSerializer.Serialize(section.Fields.Select(f => f.FieldGuid).ToArray());

            var subSections = source.SubSections ?? new();
            if (subSections.Count > 0)
            {
                if (parent != null)
                {
                    build.Errors.Add($"{where} has sub-sections of its own; only one level is loaded and rendered.");
                }
                else
                {
                    if (!source.IsCollection)
                        build.Errors.Add($"{where} has subSections but is not a collection; sub-sections are only rendered as a collection's item template.");
                    else if (fields.Count > 0)
                        build.Warnings.Add($"{where} has fields and subSections; its own fields are the item template, the sub-sections are never shown.");
                    else if (subSections.Count > 1)
                        build.Warnings.Add($"{where} has {subSections.Count} sub-sections; only the first is shown (as the item template).");

                    for (int k = 0; k < subSections.Count; k++)
                    {
                        section.SubSections.Add(BuildSection(subSections[k], $"#{k + 1}", build, section, root));
                    }
                }
            }

            return section;
        }

        private DisplayField BuildField(
            PortableDisplayFieldDto source,
            string sectionWhere,
            int index,
            ImportBuild build,
            string? dataType,
            string? relBase,
            bool editableHere)
        {
            var fieldName = BlankToNull(source.FieldName);
            var label = BlankToNull(source.Label);
            var where = $"{sectionWhere}, field '{label ?? fieldName ?? $"#{index + 1}"}'";
            if (label == null) build.Errors.Add($"{where}: label is required.");
            CheckLength(label, 200, $"{where}: label", build);
            CheckLength(fieldName, 200, $"{where}: fieldName", build);
            CheckLength(source.Description, 500, $"{where}: description", build);

            var fieldGuid = ReadGuid(source.FieldGuid, $"{where} fieldGuid", build);
            if (fieldGuid == Guid.Empty) fieldGuid = Guid.NewGuid();
            else if (build.FieldGuids.Contains(fieldGuid))
                build.Errors.Add($"{where}: fieldGuid {fieldGuid} is used twice in the file.");
            build.FieldGuids.Add(fieldGuid);

            var rel = BlankToNull(source.RelatedEntityPropertyName);
            var relType = BlankToNull(source.RelatedEntityTypeName);
            var template = string.IsNullOrWhiteSpace(source.TemplateText) ? null : source.TemplateText;
            var fieldType = BlankToNull(source.FieldType);
            CheckLength(rel, 200, $"{where}: relatedEntityPropertyName", build);
            CheckLength(relType, 200, $"{where}: relatedEntityTypeName", build);
            CheckLength(fieldType, 50, $"{where}: fieldType", build);
            CheckRelatedPair(rel, relType, where, build);
            var warn = ReadMetadataCheck(source.MetadataCheck, where, build);
            if (source.DisplayConditionGroups is { Count: > 0 })
                build.Errors.Add($"{where}: display conditions are not supported by the model yet; displayConditionGroups must be empty.");

            // E1, E5.
            if (fieldName == null && template == null)
                build.Errors.Add($"{where} needs a fieldName or a templateText.");
            if (fieldType != null && !KnownFieldTypes.Contains(fieldType))
                build.Warnings.Add($"{where}: fieldType '{fieldType}' is not one the page formats ({string.Join(", ", KnownFieldTypes)}); it is shown as text.");

            // M3: the field's own relatedEntityPropertyName, then fieldName on the entity it resolves to.
            var valueOwner = dataType;
            if (rel != null)
            {
                CheckRelatedProperty(relBase, rel, relType, collection: false, where, warn, build);
                valueOwner = relType;
            }

            if (fieldName != null)
            {
                var property = CheckPath(valueOwner, fieldName, where, warn, build, message => build.Mismatch(warn, message));
                if (property != null)
                {
                    if (IsObjectValue(property))
                        build.Warnings.Add($"{where}: {fieldName} is a {(property.FieldType == "List" ? "list" : "related object")}, which the page shows as [object Object]; show a property of it (a related field or section) instead.");
                    else if (fieldType != null && FormatFamily(fieldType) is { } want && MetadataFamily(property) is { } have && want != have)
                        build.Note(warn, $"{where}: fieldType '{fieldType}' but {fieldName} is {property.FieldType} in the model; the value is formatted as {fieldType}.");
                }
            }

            if (template != null) CheckTemplate(template, valueOwner, where, warn, build);

            // E6: hot edit writes [fieldName] onto the root entity and sends it to the entity's update endpoint.
            if (source.IsEditableInDisplay)
            {
                var reasons = new List<string>();
                if (!editableHere) reasons.Add("it is not in a top-level section without relatedEntityPropertyName and isCollection (the save would write the value onto the root entity)");
                if (fieldName == null || fieldName.Contains('.')) reasons.Add("it needs a fieldName without dots");
                if (rel != null) reasons.Add("it has a relatedEntityPropertyName");
                if (template != null) reasons.Add("it has a templateText");
                if (fieldType == null || !EditableFieldTypes.Contains(fieldType)) reasons.Add("its fieldType is not String, Integer, Number or Boolean");
                if (reasons.Count > 0)
                    build.Errors.Add($"{where} can't be isEditableInDisplay: {string.Join("; ", reasons)}.");
                else if (dataType != null && IsGetOnly(dataType, fieldName!))
                    build.Warnings.Add($"{where} is isEditableInDisplay, but {dataType}.{fieldName} is computed; the save would be ignored.");
            }

            return new DisplayField
            {
                FieldGuid = fieldGuid,
                FieldName = fieldName,
                Label = label ?? string.Empty,
                Description = BlankToNull(source.Description),
                TemplateText = template,
                FieldType = fieldType,
                RelatedEntityPropertyName = rel,
                RelatedEntityTypeName = relType,
                IsEditableInDisplay = source.IsEditableInDisplay,
                IsReusable = source.IsReusable,
                CreatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// M2/M3: <paramref name="property"/> must be a navigation of <paramref name="owner"/> whose type is the declared
        /// related type; a section's isCollection must match whether it is a list. Later checks go on with the declared
        /// type either way, so a DTO-only navigation (metadataCheck "warn") still has its fields checked.
        /// </summary>
        private static void CheckRelatedProperty(string? owner, string property, string? declaredType, bool collection, string where, bool warn, ImportBuild build)
        {
            var meta = CheckPath(owner, property, where, warn, build, message => build.Mismatch(warn, message));
            if (meta == null) return;

            var shown = $"{owner}.{property}";
            if (!meta.IsRelatedEntity || meta.RelatedEntityType == null)
            {
                build.Mismatch(warn, $"{where}: {shown} is not a related entity in the model.");
                return;
            }
            if (meta.FieldType != "List" && meta.FieldType != meta.RelatedEntityType)
            {
                build.Mismatch(warn, $"{where}: {shown} is the {meta.RelatedEntityType} id ({meta.FieldType}), not the related object; use its navigation property.");
                return;
            }
            if (declaredType != null && !string.Equals(meta.RelatedEntityType, declaredType, StringComparison.OrdinalIgnoreCase))
                build.Mismatch(warn, $"{where}: relatedEntityTypeName is '{declaredType}' but {shown} is {(meta.FieldType == "List" ? "a list of " : "a ")}{meta.RelatedEntityType} in the model.");

            var isList = meta.FieldType == "List";
            if (collection && !isList)
                build.Mismatch(warn, $"{where}: isCollection is true but {shown} is a single {meta.RelatedEntityType}.");
            else if (!collection && isList)
                build.Mismatch(warn, $"{where}: {shown} is a list; use a section with isCollection true.");
        }

        /// <summary>
        /// Follows a (dotted) property path from an entity through related types, case-insensitively as the renderer
        /// does. Returns the last property, or null when the path can't be followed (reported through
        /// <paramref name="report"/>; a type without metadata is a warning: nothing to check against).
        /// </summary>
        private static FieldMetadataDto? CheckPath(string? owner, string path, string where, bool warn, ImportBuild build, Action<string> report)
        {
            if (owner == null) return null;
            var segments = path.Split('.');
            var current = owner;
            for (int i = 0; i < segments.Length; i++)
            {
                var meta = build.Meta(current);
                if (meta == null)
                {
                    build.Note(warn, $"{where}: {current} has no metadata (not a [FormConfigurableEntity] model), so '{path}' was not checked.");
                    return null;
                }

                var segment = segments[i];
                var property = meta.Fields.FirstOrDefault(f => string.Equals(f.FieldName, segment, StringComparison.OrdinalIgnoreCase));
                if (property == null)
                {
                    report($"{where}: {meta.EntityName} has no property '{segment}'{(segments.Length > 1 ? $" (in '{path}')" : string.Empty)}.");
                    return null;
                }
                if (i == segments.Length - 1) return property;

                if (!property.IsRelatedEntity || property.RelatedEntityType == null || property.FieldType == "List")
                {
                    report($"{where}: '{path}' can't go on after {meta.EntityName}.{property.FieldName}, which is not a single related entity.");
                    return null;
                }
                current = property.RelatedEntityType;
            }
            return null;
        }

        /// <summary>E4 (syntax, errors) and M5 (paths against metadata, warnings) for "${...}" placeholders.</summary>
        private static void CheckTemplate(string template, string? owner, string where, bool warn, ImportBuild build)
        {
            var position = 0;
            while (true)
            {
                var start = template.IndexOf("${", position, StringComparison.Ordinal);
                if (start < 0) break;
                var end = template.IndexOf('}', start + 2);
                if (end < 0)
                {
                    build.Errors.Add($"{where}: templateText has a '${{' that is never closed.");
                    break;
                }
                position = end + 1;

                var expression = template.Substring(start + 2, end - start - 2).Trim();
                if (expression.Length == 0)
                {
                    build.Errors.Add($"{where}: templateText has an empty '${{}}'.");
                    continue;
                }

                // An expression with an operator is evaluated left to right; every operand is a number or a path.
                var operands = Regex.Split(expression, @"[+\-*/]").Select(o => o.Trim()).ToList();
                foreach (var operand in operands)
                {
                    if (operand.Length == 0)
                    {
                        build.Errors.Add($"{where}: templateText '${{{expression}}}' has an operator without a value on one side.");
                        continue;
                    }
                    if (TemplateNumberPattern.IsMatch(operand)) continue;
                    if (!TemplatePathPattern.IsMatch(operand))
                    {
                        build.Errors.Add($"{where}: templateText '${{{expression}}}': '{operand}' is not a property path or a number.");
                        continue;
                    }

                    var segments = operand.Split('.');
                    var counts = segments.Length > 1 && segments[^1] == "Count";
                    var upper = segments.Take(counts ? segments.Length - 1 : segments.Length).FirstOrDefault(s => char.IsUpper(s[0]));
                    if (upper != null)
                        build.Warnings.Add($"{where}: templateText path '{operand}' starts '{upper}' with a capital; template paths are case-sensitive camelCase JSON names, so it prints nothing.");

                    var path = counts ? string.Join('.', segments.Take(segments.Length - 1)) : operand;
                    var property = CheckPath(owner, path, where, warn, build, message => build.Note(warn, $"{message} (templateText)"));
                    if (property != null && counts && property.FieldType != "List")
                        build.Note(warn, $"{where}: templateText '{operand}' counts {path}, which is not a list.");
                }
            }
        }

        /// <summary>E3: a JSON object of the seven show…Button booleans, stored verbatim.</summary>
        private static string CheckButtons(string? json, string? relatedEntityTypeName, string where, ImportBuild build)
        {
            if (string.IsNullOrWhiteSpace(json)) return "{}";
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    build.Errors.Add($"{where}: actionButtonsConfigJson must be a JSON object.");
                    return json;
                }

                var anyShown = false;
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (!KnownButtonKeys.Contains(property.Name))
                    {
                        build.Warnings.Add($"{where}: actionButtonsConfigJson key '{property.Name}' is not one the page reads ({string.Join(", ", KnownButtonKeys)}).");
                        continue;
                    }
                    if (property.Value.ValueKind != JsonValueKind.True && property.Value.ValueKind != JsonValueKind.False)
                    {
                        build.Warnings.Add($"{where}: actionButtonsConfigJson '{property.Name}' should be true or false.");
                        continue;
                    }
                    if (property.Value.ValueKind != JsonValueKind.True) continue;
                    anyShown = true;
                    if (HiddenButtonKeys.Contains(property.Name))
                        build.Warnings.Add($"{where}: {property.Name} is hidden on the page until relationship actions exist (KNG-148).");
                }

                if (anyShown && relatedEntityTypeName == null)
                    build.Warnings.Add($"{where}: action buttons are only shown on sections with a relatedEntityTypeName; these never show.");
            }
            catch (JsonException ex)
            {
                build.Errors.Add($"{where}: actionButtonsConfigJson is not valid JSON ({ex.Message}).");
            }
            return json;
        }

        private static void CheckRelatedPair(string? rel, string? relType, string where, ImportBuild build)
        {
            if (rel != null && relType == null)
                build.Errors.Add($"{where}: relatedEntityPropertyName needs a relatedEntityTypeName (the type the buttons and checks use).");
            else if (rel == null && relType != null)
                build.Errors.Add($"{where}: relatedEntityTypeName must be empty without a relatedEntityPropertyName.");
        }

        private static bool ReadMetadataCheck(string? value, string where, ImportBuild build)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("error", StringComparison.OrdinalIgnoreCase)) return false;
            if (value.Trim().Equals("warn", StringComparison.OrdinalIgnoreCase)) return true;
            build.Errors.Add($"{where}: metadataCheck '{value}' is not \"error\" or \"warn\".");
            return false;
        }

        private static void CheckLength(string? value, int max, string what, ImportBuild build)
        {
            if (value != null && value.Trim().Length > max)
                build.Errors.Add($"{what} is longer than {max} characters.");
        }

        /// <summary>A navigation object or a collection: the renderer's String(value) shows [object Object].</summary>
        private static bool IsObjectValue(FieldMetadataDto property) =>
            property.FieldType == "List" || (property.IsRelatedEntity && property.FieldType == property.RelatedEntityType);

        /// <summary>How the page formats a fieldType (DisplayField.tsx formatValue); null = not compared.</summary>
        private static string? FormatFamily(string fieldType) => fieldType.ToLowerInvariant() switch
        {
            "integer" or "number" or "decimal" => "number",
            "boolean" => "boolean",
            "datetime" or "date" => "date",
            "string" or "enum" => "text",
            _ => null
        };

        private static string? MetadataFamily(FieldMetadataDto property) => property.FieldType switch
        {
            "Integer" or "Decimal" or "Float" => "number",
            "Boolean" => "boolean",
            "DateTime" or "DateTimeOffset" or "DateOnly" => "date",
            "String" or "Enum" or "Guid" => "text",
            _ => null
        };

        private static bool IsGetOnly(string entityName, string propertyName)
        {
            if (!ModelTypes.Value.TryGetValue(entityName, out var type)) return false;
            var property = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            return property != null && property.SetMethod?.IsPublic != true;
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

        private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static DisplayConfigurationImportResultDto Invalid(bool dryRun, bool asDraft, string error)
        {
            var result = new DisplayConfigurationImportResultDto { DryRun = dryRun, IsDraft = asDraft };
            result.Errors.Add(error);
            return Finish(result);
        }

        private static DisplayConfigurationImportResultDto Finish(DisplayConfigurationImportResultDto result, DisplayConfiguration? currentDefault = null)
        {
            if (result.Errors.Count > 0)
            {
                result.Status = FormConfigurationImportStatus.Invalid;
                result.Message = $"{result.Errors.Count} problem(s) found; nothing was written. {result.Errors[0]}";
                return result;
            }

            result.Status = FormConfigurationImportStatus.DryRunPassed;
            result.Message = $"Dry run passed: nothing was written. An import would create it {(result.IsDraft ? "as a draft" : "published")} and " +
                             (result.IsDefault
                                 ? (result.ReplacedDefaultId != null ? $"the default, replacing '{result.ReplacedDefaultName}'." : "the default (the entity has none).")
                                 : $"not the default ('{currentDefault?.Name}' stays the default).");
            return result;
        }

        // ---------------------------------------------------------------- bundled seeds

        public async Task<IReadOnlyList<DisplayConfigurationSeedDto>> ListSeedsAsync()
        {
            var seeds = new List<DisplayConfigurationSeedDto>();
            var guids = new Dictionary<Guid, DisplayConfigurationSeedDto>();

            foreach (var path in SeedFiles())
            {
                var seed = new DisplayConfigurationSeedDto { Name = Path.GetFileNameWithoutExtension(path) };
                seeds.Add(seed);
                try
                {
                    var file = JsonSerializer.Deserialize<PortableDisplayConfigurationDto>(await File.ReadAllTextAsync(path), FormConfigurationPortabilityService.JsonOptions);
                    if (file == null) { seed.Error = "The file is empty."; continue; }

                    var sections = (file.Sections ?? new()).Concat((file.Sections ?? new()).SelectMany(s => s.SubSections ?? new())).ToList();
                    seed.ConfigurationGuid = file.ConfigurationGuid;
                    seed.EntityTypeName = file.EntityTypeName;
                    seed.ConfigurationName = file.Name;
                    seed.Description = file.Description;
                    seed.SectionCount = sections.Count;
                    seed.FieldCount = sections.Sum(s => s.Fields?.Count ?? 0);
                    if (file.Format != PortableDisplayConfigurationDto.CurrentFormat)
                        seed.Error = $"Unknown format '{file.Format ?? "(missing)"}'.";
                    else if (Guid.TryParse(file.ConfigurationGuid, out var guid))
                        guids[guid] = seed;
                    else
                        seed.Error = "configurationGuid is missing or not a GUID.";
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    seed.Error = $"Not a readable {PortableDisplayConfigurationDto.CurrentFormat} file: {ex.Message}";
                }
            }

            if (guids.Count > 0)
            {
                var keys = guids.Keys.ToList();
                var imported = await _db.DisplayConfigurations.AsNoTracking()
                    .Where(c => keys.Contains(c.ConfigurationGuid))
                    .Select(c => new { c.Id, c.ConfigurationGuid })
                    .ToListAsync();
                foreach (var row in imported)
                {
                    guids[row.ConfigurationGuid].Imported = true;
                    guids[row.ConfigurationGuid].ImportedDisplayConfigurationId = row.Id.ToString();
                }
            }

            return seeds;
        }

        public async Task<DisplayConfigurationImportResultDto?> ImportSeedAsync(string name, bool dryRun, bool makeDefault, bool asDraft)
        {
            var path = SeedPath(name);
            if (path == null) return null;
            return await ImportJsonAsync(await File.ReadAllTextAsync(path), dryRun, makeDefault, asDraft);
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
    }
}
