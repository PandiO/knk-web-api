using System;
using System.Collections.Generic;
using knkwebapi_v2.Enums;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    public class FieldValidationDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("formFieldId")]
        public string? FormFieldId { get; set; }
        [JsonPropertyName("validationType")]
        public ValidationType ValidationType { get; set; }
        [JsonPropertyName("parametersJson")]
        public string? ParametersJson { get; set; }
        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }
        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }

    public class StepConditionDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("formStepId")]
        public string? FormStepId { get; set; }
        // "Entry" or "Completion"
        [JsonPropertyName("conditionType")]
        public string ConditionType { get; set; } = "Entry";
        [JsonPropertyName("conditionJson")]
        public string ConditionJson { get; set; } = "{}";
        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }
        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }

    public class FormFieldDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("fieldGuid")]
        public string? FieldGuid { get; set; }
        [JsonPropertyName("formStepId")]
        public string? FormStepId { get; set; }
        [JsonPropertyName("fieldName")]
        public string FieldName { get; set; } = null!;
        [JsonPropertyName("label")]
        public string Label { get; set; } = null!;
        [JsonPropertyName("placeholder")]
        public string? Placeholder { get; set; }
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        [JsonPropertyName("fieldType")]
        public FieldType FieldType { get; set; }
        [JsonPropertyName("elementType")]
        public FieldType? ElementType { get; set; }
        [JsonPropertyName("defaultValue")]
        public string? DefaultValue { get; set; }
        [JsonPropertyName("isRequired")]
        public bool IsRequired { get; set; }
        [JsonPropertyName("isReadOnly")]
        public bool IsReadOnly { get; set; }
        [JsonPropertyName("order")]
        public int Order { get; set; }
        [JsonPropertyName("dependencyConditionJson")]
        public string? DependencyConditionJson { get; set; }
        [JsonPropertyName("objectType")]
        public string? ObjectType { get; set; }
        [JsonPropertyName("subConfigurationId")]
        public string? SubConfigurationId { get; set; }
        [JsonPropertyName("incrementValue")]
        public int? IncrementValue { get; set; }
        [JsonPropertyName("isReusable")]
        public bool IsReusable { get; set; }
        [JsonPropertyName("sourceFieldId")]
        public string? SourceFieldId { get; set; }
        [JsonPropertyName("isLinkedToSource")]
        public bool IsLinkedToSource { get; set; }
        [JsonPropertyName("hasCompatibilityIssues")]
        public bool HasCompatibilityIssues { get; set; }
        [JsonPropertyName("compatibilityIssues")]
        public List<string>? CompatibilityIssues { get; set; }
        [JsonPropertyName("validations")]
        public List<FieldValidationDto> Validations { get; set; } = new();
        [JsonPropertyName("settingsJson")]
        public string? SettingsJson { get; set; }
        [JsonPropertyName("minSelection")]
        public int? MinSelection { get; set; }
        [JsonPropertyName("maxSelection")]
        public int? MaxSelection { get; set; }
        [JsonPropertyName("canCreate")]
        public bool? CanCreate { get; set; }
        [JsonPropertyName("enumType")]
        public string? EnumType { get; set; }
        [JsonPropertyName("displayConditionGroups")]
        public List<DisplayConditionGroupDto> DisplayConditionGroups { get; set; } = new();
    }

    public class FormStepDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("stepGuid")]
        public string? StepGuid { get; set; }
        [JsonPropertyName("formConfigurationId")]
        public string? FormConfigurationId { get; set; }
        [JsonPropertyName("stepName")]
        public string StepName { get; set; } = null!;
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        [JsonPropertyName("order")]
        public int Order { get; set; }
        [JsonPropertyName("fieldOrderJson")]
        public string? FieldOrderJson { get; set; }
        [JsonPropertyName("isReusable")]
        public bool IsReusable { get; set; }
        [JsonPropertyName("sourceStepId")]
        public string? SourceStepId { get; set; }
        [JsonPropertyName("isLinkedToSource")]
        public bool IsLinkedToSource { get; set; }
        [JsonPropertyName("hasCompatibilityIssues")]
        public bool HasCompatibilityIssues { get; set; }
        [JsonPropertyName("stepLevelIssues")]
        public List<string>? StepLevelIssues { get; set; }
        [JsonPropertyName("isManyToManyRelationship")]
        public bool IsManyToManyRelationship { get; set; }
        [JsonPropertyName("relatedEntityPropertyName")]
        public string? RelatedEntityPropertyName { get; set; }
        [JsonPropertyName("joinEntityType")]
        public string? JoinEntityType { get; set; }
        [JsonPropertyName("subConfigurationId")]
        public string? SubConfigurationId { get; set; }
        [JsonPropertyName("parentStepId")]
        public string? ParentStepId { get; set; }
        [JsonPropertyName("childFormSteps")]
        public List<FormStepDto> ChildFormSteps { get; set; } = new();
        [JsonPropertyName("fields")]
        public List<FormFieldDto> Fields { get; set; } = new();
        [JsonPropertyName("conditions")]
        public List<StepConditionDto> Conditions { get; set; } = new();
        [JsonPropertyName("displayConditionGroups")]
        public List<DisplayConditionGroupDto> DisplayConditionGroups { get; set; } = new();
    }

    public class FormConfigurationDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("entityTypeName")]
        public string EntityTypeName { get; set; } = null!;
        [JsonPropertyName("configurationName")]
        public string ConfigurationName { get; set; } = null!;
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        [JsonPropertyName("isDefault")]
        public bool IsDefault { get; set; }
        [JsonPropertyName("stepOrderJson")]
        public string? StepOrderJson { get; set; }
        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
        [JsonPropertyName("createdAt")]
        public string? CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }
        [JsonPropertyName("steps")]
        public List<FormStepDto> Steps { get; set; } = new();
    }

    public class FormSubmissionProgressDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("formConfigurationId")]
        public string FormConfigurationId { get; set; } = null!;
        [JsonPropertyName("userId")]
        public string? UserId { get; set; } = null!;
        [JsonPropertyName("entityTypeName")]
        public string EntityTypeName { get; set; } = null!;
        [JsonPropertyName("entityId")]
        public string? EntityId { get; set; }
        [JsonPropertyName("currentStepIndex")]
        public int CurrentStepIndex { get; set; }
        [JsonPropertyName("currentStepDataJson")]
        public string? CurrentStepDataJson { get; set; }
        [JsonPropertyName("allStepsDataJson")]
        public string? AllStepsDataJson { get; set; }
        [JsonPropertyName("parentProgressId")]
        public string? ParentProgressId { get; set; }
        [JsonPropertyName("status")]
        public FormSubmissionStatus Status { get; set; } = FormSubmissionStatus.InProgress;
        [JsonPropertyName("createdAt")]
        public string? CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }
        [JsonPropertyName("completedAt")]
        public string? CompletedAt { get; set; }
        [JsonPropertyName("configuration")]
        public FormConfigurationDto? Configuration { get; set; }
        [JsonPropertyName("childProgresses")]
        public List<FormSubmissionProgressDto>? ChildProgresses { get; set; }
    }

    public class FormSubmissionProgressOverviewDto
    {
        [JsonPropertyName("totalProgresses")]
        public int TotalProgresses { get; set; }
        [JsonPropertyName("inProgressCount")]
        public int InProgressCount { get; set; }
        [JsonPropertyName("completedCount")]
        public int CompletedCount { get; set; }
        [JsonPropertyName("abandonedCount")]
        public int AbandonedCount { get; set; }
    }

    /// <summary>
    /// This DTO provides a summary view of a form submission progress,
    /// optimized for listing multiple progresses without detailed data.
    /// It is not intended to include full step data or configuration details.
    /// </summary>
    public class FormSubmissionProgressSummaryDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("formConfigurationId")]
        public string FormConfigurationId { get; set; } = null!;
        [JsonPropertyName("formConfigurationName")]
        public string FormConfigurationName { get; set; } = null!;
        [JsonPropertyName("userId")]
        public string? UserId { get; set; }
        [JsonPropertyName("createdByUsername")]
        public string? CreatedByUsername { get; set; }
        [JsonPropertyName("entityTypeName")]
        public string? EntityTypeName { get; set; }
        [JsonPropertyName("entityId")]
        public string? EntityId { get; set; }
        [JsonPropertyName("parentProgressId")]
        public string? ParentProgressId { get; set; }
        [JsonPropertyName("currentStepIndex")]
        public int CurrentStepIndex { get; set; }
        [JsonPropertyName("status")]
        public FormSubmissionStatus Status { get; set; }
        [JsonPropertyName("createdAt")]
        public string? CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }
    }
}
