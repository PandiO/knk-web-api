using System.Collections.Generic;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// KNG-138: one FormConfiguration as a portable file (format "knk-formconfig/1"). GUIDs instead of
    /// database ids; steps and fields in array order; display conditions by source field GUID;
    /// validation rules with a symbolic dependsOn; sub configurations by reference. Written by
    /// GET api/FormConfigurations/{id}/export, read by the import and bundled-seed endpoints.
    /// Spec: knk-workspace docs/specs/form-configurations/DEFAULT_FORM_SEEDS.md §3.2.
    /// </summary>
    public class PortableFormConfigurationDto
    {
        public const string CurrentFormat = "knk-formconfig/1";

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
        /// <summary>Array order = step order.</summary>
        [JsonPropertyName("steps")]
        public List<PortableFormStepDto> Steps { get; set; } = new();
        /// <summary>Created after the fields exist, keyed by field GUID.</summary>
        [JsonPropertyName("validationRules")]
        public List<PortableValidationRuleDto> ValidationRules { get; set; } = new();
        /// <summary>Export notes for the reviewer (what was not carried and why). Ignored by the import.</summary>
        [JsonPropertyName("notes")]
        public List<string>? Notes { get; set; }
    }

    /// <summary>A sub configuration (M2M join entry or child form) by GUID; entity/name are for readers and a fallback.</summary>
    public class PortableConfigurationRefDto
    {
        [JsonPropertyName("configurationGuid")]
        public string? ConfigurationGuid { get; set; }
        [JsonPropertyName("entityTypeName")]
        public string? EntityTypeName { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class PortableFormStepDto
    {
        [JsonPropertyName("stepGuid")]
        public string? StepGuid { get; set; }
        [JsonPropertyName("stepName")]
        public string? StepName { get; set; }
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        [JsonPropertyName("isManyToManyRelationship")]
        public bool IsManyToManyRelationship { get; set; }
        [JsonPropertyName("relatedEntityPropertyName")]
        public string? RelatedEntityPropertyName { get; set; }
        [JsonPropertyName("joinEntityType")]
        public string? JoinEntityType { get; set; }
        [JsonPropertyName("subConfigurationRef")]
        public PortableConfigurationRefDto? SubConfigurationRef { get; set; }
        [JsonPropertyName("conditions")]
        public List<PortableStepConditionDto>? Conditions { get; set; }
        [JsonPropertyName("displayConditionGroups")]
        public List<PortableDisplayConditionGroupDto>? DisplayConditionGroups { get; set; }
        /// <summary>Array order = field order.</summary>
        [JsonPropertyName("fields")]
        public List<PortableFormFieldDto> Fields { get; set; } = new();
        /// <summary>Inline child steps of a many-to-many step (rare).</summary>
        [JsonPropertyName("childFormSteps")]
        public List<PortableFormStepDto>? ChildFormSteps { get; set; }
    }

    public class PortableFormFieldDto
    {
        [JsonPropertyName("fieldGuid")]
        public string? FieldGuid { get; set; }
        [JsonPropertyName("fieldName")]
        public string? FieldName { get; set; }
        [JsonPropertyName("label")]
        public string? Label { get; set; }
        [JsonPropertyName("placeholder")]
        public string? Placeholder { get; set; }
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        [JsonPropertyName("fieldType")]
        public FieldType FieldType { get; set; }
        [JsonPropertyName("elementType")]
        public FieldType? ElementType { get; set; }
        [JsonPropertyName("objectType")]
        public string? ObjectType { get; set; }
        [JsonPropertyName("enumType")]
        public string? EnumType { get; set; }
        [JsonPropertyName("defaultValue")]
        public string? DefaultValue { get; set; }
        [JsonPropertyName("isRequired")]
        public bool IsRequired { get; set; }
        [JsonPropertyName("isReadOnly")]
        public bool IsReadOnly { get; set; }
        [JsonPropertyName("minSelection")]
        public int? MinSelection { get; set; }
        [JsonPropertyName("maxSelection")]
        public int? MaxSelection { get; set; }
        [JsonPropertyName("canCreate")]
        public bool? CanCreate { get; set; }
        [JsonPropertyName("incrementValue")]
        public int? IncrementValue { get; set; }
        [JsonPropertyName("dependencyConditionJson")]
        public string? DependencyConditionJson { get; set; }
        /// <summary>Stored verbatim (worldTask, ownedChildCollection, pickerFilters, enumValues, ...).</summary>
        [JsonPropertyName("settingsJson")]
        public string? SettingsJson { get; set; }
        [JsonPropertyName("subConfigurationRef")]
        public PortableConfigurationRefDto? SubConfigurationRef { get; set; }
        /// <summary>Simple per-field validations (FieldValidation rows).</summary>
        [JsonPropertyName("validations")]
        public List<PortableFieldValidationDto>? Validations { get; set; }
        [JsonPropertyName("displayConditionGroups")]
        public List<PortableDisplayConditionGroupDto>? DisplayConditionGroups { get; set; }
    }

    public class PortableStepConditionDto
    {
        /// <summary>"Entry" or "Completion" (as in StepConditionDto).</summary>
        [JsonPropertyName("conditionType")]
        public string? ConditionType { get; set; }
        [JsonPropertyName("conditionJson")]
        public string? ConditionJson { get; set; }
        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }
    }

    public class PortableFieldValidationDto
    {
        [JsonPropertyName("validationType")]
        public ValidationType ValidationType { get; set; }
        [JsonPropertyName("parametersJson")]
        public string? ParametersJson { get; set; }
        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }
    }

    /// <summary>Array order = group order.</summary>
    public class PortableDisplayConditionGroupDto
    {
        [JsonPropertyName("innerLogic")]
        public DisplayConditionLogic InnerLogic { get; set; } = DisplayConditionLogic.And;
        [JsonPropertyName("combineWithPreviousLogic")]
        public DisplayConditionLogic CombineWithPreviousLogic { get; set; } = DisplayConditionLogic.Or;
        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; } = true;
        /// <summary>Array order = condition order.</summary>
        [JsonPropertyName("conditions")]
        public List<PortableDisplayConditionDto> Conditions { get; set; } = new();
    }

    public class PortableDisplayConditionDto
    {
        [JsonPropertyName("sourceFieldGuid")]
        public string? SourceFieldGuid { get; set; }
        [JsonPropertyName("operator")]
        public ConditionOperator Operator { get; set; } = ConditionOperator.Equals;
        [JsonPropertyName("valueJson")]
        public string? ValueJson { get; set; }
    }

    public class PortableValidationRuleDto
    {
        [JsonPropertyName("fieldGuid")]
        public string? FieldGuid { get; set; }
        [JsonPropertyName("validationType")]
        public string? ValidationType { get; set; }
        [JsonPropertyName("dependsOn")]
        public PortableFieldRefDto? DependsOn { get; set; }
        [JsonPropertyName("dependencyPath")]
        public string? DependencyPath { get; set; }
        [JsonPropertyName("configJson")]
        public string? ConfigJson { get; set; }
        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }
        [JsonPropertyName("successMessage")]
        public string? SuccessMessage { get; set; }
        [JsonPropertyName("isBlocking")]
        public bool IsBlocking { get; set; } = true;
        [JsonPropertyName("requiresDependencyFilled")]
        public bool RequiresDependencyFilled { get; set; }
    }

    /// <summary>
    /// A field by GUID. Without configurationGuid (or with this file's) it is a field of this file;
    /// otherwise a field of that configuration in the database (e.g. a "parent form" field).
    /// </summary>
    public class PortableFieldRefDto
    {
        [JsonPropertyName("fieldGuid")]
        public string? FieldGuid { get; set; }
        [JsonPropertyName("configurationGuid")]
        public string? ConfigurationGuid { get; set; }
        /// <summary>For readers only.</summary>
        [JsonPropertyName("fieldName")]
        public string? FieldName { get; set; }
    }

    public static class FormConfigurationImportStatus
    {
        public const string Imported = "Imported";
        public const string DryRunPassed = "DryRunPassed";
        public const string AlreadyImported = "AlreadyImported";
        public const string Invalid = "Invalid";
    }

    /// <summary>Result of an import or dry run. Nothing is written unless Status is Imported.</summary>
    public class FormConfigurationImportResultDto
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
        [JsonPropertyName("formConfigurationId")]
        public string? FormConfigurationId { get; set; }
        /// <summary>Whether the configuration is (or would be) the entity's default.</summary>
        [JsonPropertyName("isDefault")]
        public bool IsDefault { get; set; }
        /// <summary>The previous default that is (or would be) unset because makeDefault was asked.</summary>
        [JsonPropertyName("replacedDefaultId")]
        public string? ReplacedDefaultId { get; set; }
        [JsonPropertyName("replacedDefaultName")]
        public string? ReplacedDefaultName { get; set; }
        [JsonPropertyName("stepCount")]
        public int StepCount { get; set; }
        [JsonPropertyName("fieldCount")]
        public int FieldCount { get; set; }
        [JsonPropertyName("validationRuleCount")]
        public int ValidationRuleCount { get; set; }
        [JsonPropertyName("errors")]
        public List<string> Errors { get; set; } = new();
        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>One bundled seed file in Data/FormConfigurationSeeds/.</summary>
    public class FormConfigurationSeedDto
    {
        /// <summary>File name without ".json"; the {name} of seeds/{name}/import.</summary>
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
        [JsonPropertyName("stepCount")]
        public int StepCount { get; set; }
        [JsonPropertyName("fieldCount")]
        public int FieldCount { get; set; }
        [JsonPropertyName("imported")]
        public bool Imported { get; set; }
        [JsonPropertyName("importedFormConfigurationId")]
        public string? ImportedFormConfigurationId { get; set; }
        /// <summary>Set when the file can't be read as a knk-formconfig/1 file.</summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
