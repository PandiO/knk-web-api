using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// DTO for reading FieldValidationRule data.
    /// 
    /// PLACEHOLDER SYNTAX:
    /// Error and success messages support dynamic variable interpolation using the placeholder syntax {placeholder}.
    /// Placeholders are resolved in multiple layers across frontend and backend:
    /// 
    /// LAYER 0 (Frontend): Direct form field values
    /// - Syntax: {FieldName}
    /// - Example: {Name} resolves from current form field value
    /// - Resolved by: FormWizard component before sending to API
    /// 
    /// LAYER 1 (Backend): Single navigation with DB query
    /// - Syntax: {NavigationProperty.PropertyName}
    /// - Example: {Town.Name} fetches Town entity and extracts Name
    /// - Resolved by: PlaceholderResolutionService.ResolveLayer1Async()
    /// - Data source: Database via foreign key + Include
    /// 
    /// LAYER 2 (Backend): Multi-level navigation with Include chains
    /// - Syntax: {Navigation1.Navigation2.PropertyName}
    /// - Example: {District.Town.Name} navigates through multiple levels
    /// - Resolved by: PlaceholderResolutionService.ResolveLayer2Async()
    /// - Data source: Database with dynamic Include chains
    /// 
    /// LAYER 3 (Backend): Aggregate operations on collections
    /// - Syntax: {Navigation.Collection.AggregateOp}
    /// - Example: {Town.Districts.Count} loads collection and counts items
    /// - Resolved by: PlaceholderResolutionService.ResolveLayer3Async()
    /// - Operations: Count, First, Last, Any, Sum, Average, Max, Min
    /// 
    /// RESOLUTION FLOW:
    /// 1. Frontend builds message template with all layers marked as {placeholder}
    /// 2. For Layer 0: Frontend calls buildPlaceholderContext() to extract current form values
    /// 3. Frontend sends PlaceholderResolutionRequest to backend with Layer 0 values
    /// 4. Backend resolves Layers 1-3 using PlaceholderResolutionService
    /// 5. Backend returns PlaceholderResolutionResponse with all resolved values
    /// 6. Frontend interpolates message using interpolatePlaceholders() utility
    /// 7. FieldRenderer displays final interpolated message to user
    /// 
    /// EXAMPLE MESSAGE:
    /// - Template: "Location {coordinates} is outside {Town.Name}'s boundaries."
    /// - Layer 0 extracted: { "coordinates": "(125.5, 64.0, -350.2)" }
    /// - Backend resolves: { "Town.Name": "Springfield" }
    /// - Final: "Location (125.5, 64.0, -350.2) is outside Springfield's boundaries."
    /// </summary>
    public class FieldValidationRuleDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("formFieldId")]
        public int FormFieldId { get; set; }
        
        [JsonPropertyName("validationType")]
        public string ValidationType { get; set; } = null!;
        
        [JsonPropertyName("dependsOnFieldId")]
        public int? DependsOnFieldId { get; set; }
        
        /// <summary>
        /// Path to navigate from the dependency field value to the actual property to validate against.
        /// Supports multi-layer navigation through related entities.
        /// 
        /// EXAMPLES:
        /// - "WgRegionId" (Layer 0): Direct property of dependency field value
        /// - "Town.WgRegionId" (Layer 1): Navigate to Town entity, extract WgRegionId
        /// - "District.Town.WgRegionId" (Layer 2): Navigate through multiple relations
        /// - "Town.Districts.Count" (Layer 3): Aggregate operations on collections
        /// 
        /// If null/empty and DependsOnFieldId is set, defaults to the field name itself.
        /// </summary>
        [JsonPropertyName("dependencyPath")]
        public string? DependencyPath { get; set; }
        
        [JsonPropertyName("configJson")]
        public string ConfigJson { get; set; } = "{}";
        
        /// <summary>
        /// Error message displayed to user if validation fails.
        /// Supports multi-layer placeholder interpolation - see class documentation for placeholder syntax.
        /// 
        /// The message template can contain any combination of Layer 0/1/2/3 placeholders.
        /// Placeholders are resolved asynchronously in the validation flow.
        /// </summary>
        [JsonPropertyName("errorMessage")]
        public string ErrorMessage { get; set; } = null!;
        
        /// <summary>
        /// Success message displayed to user if validation passes.
        /// Optional; if empty, validation success just clears the error state.
        /// Supports same placeholder syntax as ErrorMessage.
        /// </summary>
        [JsonPropertyName("successMessage")]
        public string? SuccessMessage { get; set; }
        
        [JsonPropertyName("isBlocking")]
        public bool IsBlocking { get; set; }
        
        [JsonPropertyName("requiresDependencyFilled")]
        public bool RequiresDependencyFilled { get; set; }
        
        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = null!;

        /// <summary>
        /// Navigation DTO for the form field being validated.
        /// Provides display information about the field.
        /// </summary>
        [JsonPropertyName("formField")]
        public FormFieldNavDto? FormField { get; set; }

        /// <summary>
        /// Navigation DTO for the field this rule depends on.
        /// Provides display information about the dependency field.
        /// </summary>
        [JsonPropertyName("dependsOnField")]
        public FormFieldNavDto? DependsOnField { get; set; }
    }
    
    /// <summary>
    /// Navigation DTO for FormField to avoid circular references.
    /// Contains minimal information for display purposes.
    /// </summary>
    public class FormFieldNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("fieldName")]
        public string? FieldName { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }
    
    /// <summary>
    /// DTO for creating a new FieldValidationRule.
    /// See FieldValidationRuleDto for detailed documentation on placeholder syntax.
    /// </summary>
    public class CreateFieldValidationRuleDto
    {
        [JsonPropertyName("formFieldId")]
        public int FormFieldId { get; set; }
        
        [JsonPropertyName("validationType")]
        public string ValidationType { get; set; } = null!;
        
        [JsonPropertyName("dependsOnFieldId")]
        public int? DependsOnFieldId { get; set; }
                /// <summary>
        /// Path to navigate from the dependency field value to the actual property to validate against.
        /// Supports multi-layer navigation through related entities.
        /// See FieldValidationRuleDto for detailed examples.
        /// </summary>
        [JsonPropertyName("dependencyPath")]
        public string? DependencyPath { get; set; }
                [JsonPropertyName("configJson")]
        public string ConfigJson { get; set; } = "{}";
        
        /// <summary>
        /// Error message template with placeholders. See FieldValidationRuleDto for placeholder syntax.
        /// </summary>
        [JsonPropertyName("errorMessage")]
        public string ErrorMessage { get; set; } = null!;
        
        /// <summary>
        /// Success message template with placeholders. See FieldValidationRuleDto for placeholder syntax.
        /// </summary>
        [JsonPropertyName("successMessage")]
        public string? SuccessMessage { get; set; }
        
        [JsonPropertyName("isBlocking")]
        public bool IsBlocking { get; set; } = true;
        
        [JsonPropertyName("requiresDependencyFilled")]
        public bool RequiresDependencyFilled { get; set; } = false;
    }
    
    /// <summary>
    /// DTO for updating an existing FieldValidationRule.
    /// See FieldValidationRuleDto for detailed documentation on placeholder syntax.
    /// </summary>
    public class UpdateFieldValidationRuleDto
    {
        [JsonPropertyName("validationType")]
        public string ValidationType { get; set; } = null!;
        
        [JsonPropertyName("dependsOnFieldId")]
        public int? DependsOnFieldId { get; set; }

        /// <summary>
        /// Path from the dependency field's value to the property validated against (see
        /// FieldValidationRuleDto). Was missing here, so an edited path was dropped and the rule kept its
        /// old one while every other change was saved.
        /// </summary>
        [JsonPropertyName("dependencyPath")]
        public string? DependencyPath { get; set; }

        [JsonPropertyName("configJson")]
        public string ConfigJson { get; set; } = "{}";

        /// <summary>
        /// Error message template with placeholders. See FieldValidationRuleDto for placeholder syntax.
        /// </summary>
        [JsonPropertyName("errorMessage")]
        public string ErrorMessage { get; set; } = null!;

        /// <summary>
        /// Success message template with placeholders. See FieldValidationRuleDto for placeholder syntax.
        /// </summary>
        [JsonPropertyName("successMessage")]
        public string? SuccessMessage { get; set; }

        [JsonPropertyName("isBlocking")]
        public bool IsBlocking { get; set; }

        [JsonPropertyName("requiresDependencyFilled")]
        public bool RequiresDependencyFilled { get; set; }
    }

    /// <summary>
    /// Request DTO for validating a field value.
    /// </summary>
    public class ValidateFieldRequestDto
    {
        [JsonPropertyName("fieldId")]
        public int FieldId { get; set; }
        
        [JsonPropertyName("fieldValue")]
        public object? FieldValue { get; set; }
        
        [JsonPropertyName("dependencyValue")]
        public object? DependencyValue { get; set; }
        
        [JsonPropertyName("formContextData")]
        public Dictionary<string, object>? FormContextData { get; set; }
    }

    /// <summary>
    /// Request DTO for validating a field value against a specific validation rule.
    /// </summary>
    public class ValidateFieldRuleRequestDto
    {
        [JsonPropertyName("fieldValidationRuleId")]
        public int? FieldValidationRuleId { get; set; }

        [JsonPropertyName("fieldValue")]
        public object? FieldValue { get; set; }

        [JsonPropertyName("dependencyFieldValue")]
        public object? DependencyFieldValue { get; set; }

        [JsonPropertyName("currentEntityPlaceholders")]
        public Dictionary<string, string>? CurrentEntityPlaceholders { get; set; }

        [JsonPropertyName("entityId")]
        public int? EntityId { get; set; }
    }
    
    /// <summary>
    /// Response DTO containing validation result.
    /// </summary>
    public class ValidationResultDto
    {
        [JsonPropertyName("isValid")]
        public bool IsValid { get; set; }
        
        [JsonPropertyName("isBlocking")]
        public bool IsBlocking { get; set; }
        
        [JsonPropertyName("message")]
        public string Message { get; set; } = null!;
        
        [JsonPropertyName("placeholders")]
        public Dictionary<string, string>? Placeholders { get; set; }
        
        [JsonPropertyName("metadata")]
        public ValidationMetadataDto? Metadata { get; set; }
    }
    
    /// <summary>
    /// Metadata about the validation execution.
    /// </summary>
    public class ValidationMetadataDto
    {
        [JsonPropertyName("validationType")]
        public string ValidationType { get; set; } = null!;
        
        [JsonPropertyName("executedAt")]
        public string ExecutedAt { get; set; } = null!;
        
        [JsonPropertyName("dependencyFieldName")]
        public string? DependencyFieldName { get; set; }
        
        [JsonPropertyName("dependencyValue")]
        public object? DependencyValue { get; set; }

        /// <summary>
        /// Set when validation could not be completed due to a system/infrastructure problem
        /// (e.g. "PluginUnreachable") rather than a genuine business-rule failure. The frontend
        /// uses this to distinguish "this value is invalid" from "we couldn't check this value".
        /// </summary>
        [JsonPropertyName("failureReason")]
        public string? FailureReason { get; set; }
    }
    
    /// <summary>
    /// DTO for configuration health check issues.
    /// </summary>
    public class ValidationIssueDto
    {
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = null!; // "Error", "Warning", "Info"
        
        [JsonPropertyName("message")]
        public string Message { get; set; } = null!;
        
        [JsonPropertyName("fieldId")]
        public int? FieldId { get; set; }
        
        [JsonPropertyName("ruleId")]
        public int? RuleId { get; set; }
    }
}
