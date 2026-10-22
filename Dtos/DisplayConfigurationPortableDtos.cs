using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// One DisplayConfiguration as a portable file (format "knk-displayconfig/1"), the display counterpart of
    /// knk-formconfig/1 (KNG-138). GUIDs instead of database ids; sections and fields in array order; one level
    /// of inline sub-sections. Written by GET api/DisplayConfigurations/{id}/export, read by the import and
    /// bundled-seed endpoints. Spec: Data/DisplayConfigurationSeeds/README.md.
    /// </summary>
    public class PortableDisplayConfigurationDto
    {
        public const string CurrentFormat = "knk-displayconfig/1";

        [JsonPropertyName("format")]
        public string? Format { get; set; }
        [JsonPropertyName("configurationGuid")]
        public string? ConfigurationGuid { get; set; }
        [JsonPropertyName("entityTypeName")]
        public string? EntityTypeName { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        /// <summary>Top-level sections; array order = SectionOrderJson.</summary>
        [JsonPropertyName("sections")]
        public List<PortableDisplaySectionDto> Sections { get; set; } = new();
        /// <summary>Export notes for the reviewer (what was not carried and why). Ignored by the import.</summary>
        [JsonPropertyName("notes")]
        public List<string>? Notes { get; set; }
    }

    public class PortableDisplaySectionDto
    {
        [JsonPropertyName("sectionGuid")]
        public string? SectionGuid { get; set; }
        [JsonPropertyName("sectionName")]
        public string? SectionName { get; set; }
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        /// <summary>Read-DTO property of the root entity whose value the section shows.</summary>
        [JsonPropertyName("relatedEntityPropertyName")]
        public string? RelatedEntityPropertyName { get; set; }
        [JsonPropertyName("relatedEntityTypeName")]
        public string? RelatedEntityTypeName { get; set; }
        [JsonPropertyName("isCollection")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsCollection { get; set; }
        [JsonPropertyName("isReusable")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsReusable { get; set; }
        /// <summary>Stored verbatim; omitted = "{}".</summary>
        [JsonPropertyName("actionButtonsConfigJson")]
        public string? ActionButtonsConfigJson { get; set; }
        /// <summary>"error" (default) or "warn": metadata mismatches of this element are warnings.</summary>
        [JsonPropertyName("metadataCheck")]
        public string? MetadataCheck { get; set; }
        /// <summary>Reserved; must be absent or empty in /1 (the model has no display-section conditions).</summary>
        [JsonPropertyName("displayConditionGroups")]
        public List<PortableDisplayConditionGroupDto>? DisplayConditionGroups { get; set; }
        /// <summary>Array order = FieldOrderJson.</summary>
        [JsonPropertyName("fields")]
        public List<PortableDisplayFieldDto> Fields { get; set; } = new();
        /// <summary>Only on a collection section, one level deep: the first one is the item template.</summary>
        [JsonPropertyName("subSections")]
        public List<PortableDisplaySectionDto>? SubSections { get; set; }
    }

    public class PortableDisplayFieldDto
    {
        [JsonPropertyName("fieldGuid")]
        public string? FieldGuid { get; set; }
        /// <summary>Property path (may be dotted); optional when templateText is set.</summary>
        [JsonPropertyName("fieldName")]
        public string? FieldName { get; set; }
        [JsonPropertyName("relatedEntityPropertyName")]
        public string? RelatedEntityPropertyName { get; set; }
        [JsonPropertyName("relatedEntityTypeName")]
        public string? RelatedEntityTypeName { get; set; }
        [JsonPropertyName("label")]
        public string? Label { get; set; }
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        /// <summary>"${path}" placeholders (camelCase JSON names); wins over fieldName.</summary>
        [JsonPropertyName("templateText")]
        public string? TemplateText { get; set; }
        [JsonPropertyName("fieldType")]
        public string? FieldType { get; set; }
        [JsonPropertyName("isEditableInDisplay")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsEditableInDisplay { get; set; }
        [JsonPropertyName("isReusable")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsReusable { get; set; }
        [JsonPropertyName("metadataCheck")]
        public string? MetadataCheck { get; set; }
        /// <summary>Reserved, as on sections.</summary>
        [JsonPropertyName("displayConditionGroups")]
        public List<PortableDisplayConditionGroupDto>? DisplayConditionGroups { get; set; }
    }

    /// <summary>
    /// Result of a display import or dry run; same shape and statuses (FormConfigurationImportStatus) as the
    /// form import. Nothing is written unless Status is Imported.
    /// </summary>
    public class DisplayConfigurationImportResultDto
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = FormConfigurationImportStatus.Invalid;
        [JsonPropertyName("dryRun")]
        public bool DryRun { get; set; }
        /// <summary>Plain-text summary (also what the web app shows on a failed request).</summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        [JsonPropertyName("configurationGuid")]
        public string? ConfigurationGuid { get; set; }
        [JsonPropertyName("entityTypeName")]
        public string? EntityTypeName { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        /// <summary>The created configuration, or the existing one for AlreadyImported.</summary>
        [JsonPropertyName("displayConfigurationId")]
        public string? DisplayConfigurationId { get; set; }
        /// <summary>Whether the configuration is (or would be) the entity's default.</summary>
        [JsonPropertyName("isDefault")]
        public bool IsDefault { get; set; }
        /// <summary>Whether the configuration is (or would be) a draft; /display only shows published defaults.</summary>
        [JsonPropertyName("isDraft")]
        public bool IsDraft { get; set; }
        /// <summary>The previous default that is (or would be) unset because makeDefault was asked.</summary>
        [JsonPropertyName("replacedDefaultId")]
        public string? ReplacedDefaultId { get; set; }
        [JsonPropertyName("replacedDefaultName")]
        public string? ReplacedDefaultName { get; set; }
        /// <summary>All sections, sub-sections included.</summary>
        [JsonPropertyName("sectionCount")]
        public int SectionCount { get; set; }
        [JsonPropertyName("fieldCount")]
        public int FieldCount { get; set; }
        [JsonPropertyName("errors")]
        public List<string> Errors { get; set; } = new();
        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>One bundled seed file in Data/DisplayConfigurationSeeds/.</summary>
    public class DisplayConfigurationSeedDto
    {
        /// <summary>File name without ".json" (e.g. "User.display"); the {name} of seeds/{name}/import.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
        [JsonPropertyName("configurationGuid")]
        public string? ConfigurationGuid { get; set; }
        [JsonPropertyName("entityTypeName")]
        public string? EntityTypeName { get; set; }
        [JsonPropertyName("configurationName")]
        public string? ConfigurationName { get; set; }
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        [JsonPropertyName("sectionCount")]
        public int SectionCount { get; set; }
        [JsonPropertyName("fieldCount")]
        public int FieldCount { get; set; }
        [JsonPropertyName("imported")]
        public bool Imported { get; set; }
        [JsonPropertyName("importedDisplayConfigurationId")]
        public string? ImportedDisplayConfigurationId { get; set; }
        /// <summary>Set when the file can't be read as a knk-displayconfig/1 file.</summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
