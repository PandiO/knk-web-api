using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// Result of validating a single FormField against entity metadata.
    /// Used to determine if a reused field is compatible with the target entity.
    /// </summary>
    public class TemplateFieldValidationResult
    {
        [JsonPropertyName("formFieldId")]
        public int FormFieldId { get; set; }

        [JsonPropertyName("fieldName")]
        public string FieldName { get; set; } = null!;

        [JsonPropertyName("isCompatible")]
        public bool IsCompatible { get; set; }

        /// <summary>
        /// List of compatibility issues found, if any.
        /// Examples:
        /// - "Field 'LocationId' does not exist on entity 'Structure'."
        /// - "Field 'Name' type mismatch: expected 'String', got 'Integer'."
        /// - "Field 'DomainId' is object-type but entity metadata does not mark it as related."
        /// </summary>
        [JsonPropertyName("issues")]
        public List<string> Issues { get; set; } = new();
    }

    /// <summary>
    /// Result of validating a complete FormStep against entity metadata.
    /// Aggregates validation results for all fields in the step.
    /// </summary>
    public class TemplateStepValidationResult
    {
        [JsonPropertyName("formStepId")]
        public int FormStepId { get; set; }

        [JsonPropertyName("stepName")]
        public string StepName { get; set; } = null!;

        [JsonPropertyName("isCompatible")]
        public bool IsCompatible { get; set; }

        /// <summary>
        /// Validation results for each field in the step.
        /// If any field is incompatible, the overall step is incompatible.
        /// </summary>
        [JsonPropertyName("fieldResults")]
        public List<TemplateFieldValidationResult> FieldResults { get; set; } = new();
    }

    /// <summary>
    /// Result of validating an entire FormConfiguration against entity metadata.
    /// Used to block saving or applying configurations with incompatible fields.
    /// </summary>
    public class FormConfigurationValidationResult
    {
        [JsonPropertyName("formConfigurationId")]
        public int FormConfigurationId { get; set; }

        [JsonPropertyName("entityTypeName")]
        public string EntityTypeName { get; set; } = null!;

        [JsonPropertyName("isValid")]
        public bool IsValid { get; set; }

        /// <summary>
        /// High-level validation message (e.g., "FormConfiguration has 2 incompatible fields").
        /// </summary>
        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        /// <summary>
        /// Validation results for each step in the configuration.
        /// </summary>
        [JsonPropertyName("stepResults")]
        public List<TemplateStepValidationResult> StepResults { get; set; } = new();
    }
}
