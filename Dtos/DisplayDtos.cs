using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// DTO for DisplayConfiguration entity.
    /// Represents a complete display template for entity data rendering.
    /// </summary>
    public class DisplayConfigurationDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("configurationGuid")]
        public string? ConfigurationGuid { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
        
        [JsonPropertyName("entityTypeName")]
        public string EntityTypeName { get; set; } = null!;
        
        [JsonPropertyName("isDefault")]
        public bool IsDefault { get; set; }
        
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        
        [JsonPropertyName("sectionOrderJson")]
        public string? SectionOrderJson { get; set; }
        
        [JsonPropertyName("isDraft")]
        public bool IsDraft { get; set; } = true;

        /// <summary>PandiO/knk-workspace#71: the builder holds a saved, unpublished working copy. Read only.</summary>
        [JsonPropertyName("hasUnpublishedChanges")]
        public bool HasUnpublishedChanges { get; set; }

        [JsonPropertyName("draftSavedAt")]
        public string? DraftSavedAt { get; set; }

        [JsonPropertyName("publishedAt")]
        public string? PublishedAt { get; set; }
        
        [JsonPropertyName("createdAt")]
        public string? CreatedAt { get; set; }
        
        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }
        
        [JsonPropertyName("sections")]
        public List<DisplaySectionDto> Sections { get; set; } = new();
    }

    /// <summary>
    /// DTO for DisplaySection entity.
    /// Represents a grouping of display fields with optional relationship binding.
    /// </summary>
    public class DisplaySectionDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("sectionGuid")]
        public string? SectionGuid { get; set; }
        
        [JsonPropertyName("displayConfigurationId")]
        public string? DisplayConfigurationId { get; set; }
        
        [JsonPropertyName("sectionName")]
        public string SectionName { get; set; } = null!;
        
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        
        [JsonPropertyName("isReusable")]
        public bool IsReusable { get; set; }
        
        [JsonPropertyName("sourceSectionId")]
        public string? SourceSectionId { get; set; }
        
        [JsonPropertyName("isLinkedToSource")]
        public bool IsLinkedToSource { get; set; }
        
        [JsonPropertyName("fieldOrderJson")]
        public string? FieldOrderJson { get; set; }
        
        [JsonPropertyName("relatedEntityPropertyName")]
        public string? RelatedEntityPropertyName { get; set; }
        
        [JsonPropertyName("relatedEntityTypeName")]
        public string? RelatedEntityTypeName { get; set; }
        
        [JsonPropertyName("isCollection")]
        public bool IsCollection { get; set; }
        
        [JsonPropertyName("actionButtonsConfigJson")]
        public string? ActionButtonsConfigJson { get; set; }
        
        [JsonPropertyName("parentSectionId")]
        public string? ParentSectionId { get; set; }
        
        [JsonPropertyName("createdAt")]
        public string? CreatedAt { get; set; }
        
        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }
        
        [JsonPropertyName("fields")]
        public List<DisplayFieldDto> Fields { get; set; } = new();
        
        [JsonPropertyName("subSections")]
        public List<DisplaySectionDto> SubSections { get; set; } = new();
    }

    /// <summary>
    /// DTO for DisplayField entity.
    /// Represents a single display field with optional template text.
    /// </summary>
    public class DisplayFieldDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("fieldGuid")]
        public string? FieldGuid { get; set; }
        
        [JsonPropertyName("displaySectionId")]
        public string? DisplaySectionId { get; set; }
        
        [JsonPropertyName("relatedEntityPropertyName")]
        public string? RelatedEntityPropertyName { get; set; }
        
        [JsonPropertyName("relatedEntityTypeName")]
        public string? RelatedEntityTypeName { get; set; }
        
        [JsonPropertyName("fieldName")]
        public string? FieldName { get; set; }
        
        [JsonPropertyName("label")]
        public string Label { get; set; } = null!;
        
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        
        [JsonPropertyName("templateText")]
        public string? TemplateText { get; set; }
        
        [JsonPropertyName("fieldType")]
        public string? FieldType { get; set; }
        
        [JsonPropertyName("isReusable")]
        public bool IsReusable { get; set; }
        
            [JsonPropertyName("isEditableInDisplay")]
            public bool IsEditableInDisplay { get; set; }
        
        [JsonPropertyName("sourceFieldId")]
        public string? SourceFieldId { get; set; }
        
        [JsonPropertyName("isLinkedToSource")]
        public bool IsLinkedToSource { get; set; }
        
        [JsonPropertyName("createdAt")]
        public string? CreatedAt { get; set; }
        
        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Typed representation of ActionButtonsConfigJson.
    /// Used for validation and type-safe serialization.
    /// </summary>
    public class ActionButtonsConfigDto
    {
        // Common buttons
        [JsonPropertyName("showViewButton")]
        public bool ShowViewButton { get; set; }
        
        [JsonPropertyName("showEditButton")]
        public bool ShowEditButton { get; set; }
        
        // Single relationship buttons
        [JsonPropertyName("showSelectButton")]
        public bool ShowSelectButton { get; set; }
        
        [JsonPropertyName("showUnlinkButton")]
        public bool ShowUnlinkButton { get; set; }
        
        // Collection relationship buttons
        [JsonPropertyName("showAddButton")]
        public bool ShowAddButton { get; set; }
        
        [JsonPropertyName("showRemoveButton")]
        public bool ShowRemoveButton { get; set; }
        
        // Both types
        [JsonPropertyName("showCreateButton")]
        public bool ShowCreateButton { get; set; }
    }

    /// <summary>
    /// Validation result for DisplayConfiguration validation.
    /// Contains errors, warnings, and field-specific issues.
    /// </summary>
    public class DisplayValidationResultDto
    {
        [JsonPropertyName("isValid")]
        public bool IsValid { get; set; }
        
        [JsonPropertyName("errors")]
        public List<string> Errors { get; set; } = new();
        
        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();
        
        [JsonPropertyName("fieldErrors")]
        public Dictionary<string, List<string>> FieldErrors { get; set; } = new();
    }
}
