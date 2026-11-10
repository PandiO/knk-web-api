using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Service for validating FormStep and FormField compatibility against EntityMetadataDto.
    /// Ensures reused templates are compatible with target entities before saving.
    /// </summary>
    public interface IFormTemplateValidationService
    {
        /// <summary>
        /// Validate a single FormField against entity metadata.
        /// Checks if the field exists on the entity, has compatible type, and respects nullability constraints.
        /// </summary>
        /// <param name="field">The form field to validate</param>
        /// <param name="entityMetadata">Metadata for the target entity (includes parent class fields)</param>
        /// <returns>Validation result indicating compatibility and any issues found</returns>
        TemplateFieldValidationResult ValidateField(FormField field, EntityMetadataDto? entityMetadata);

        /// <summary>
        /// Validate a FormStep and all its fields against entity metadata.
        /// Iterates through all fields in the step and validates each one.
        /// The step is marked incompatible if any field fails validation.
        /// </summary>
        /// <param name="step">The form step to validate</param>
        /// <param name="entityMetadata">Metadata for the target entity (includes parent class fields)</param>
        /// <returns>Validation result with field-level details</returns>
        TemplateStepValidationResult ValidateStep(FormStep step, EntityMetadataDto? entityMetadata);

        /// <summary>
        /// Validate an entire FormConfiguration against entity metadata.
        /// This is the top-level validation that checks all steps and fields.
        /// Returns false if any field is incompatible with the entity's schema.
        /// Used before saving configurations to prevent data structure mismatches.
        /// </summary>
        /// <param name="config">The form configuration to validate</param>
        /// <param name="metadataService">Service to retrieve entity metadata (includes parent fields)</param>
        /// <returns>Comprehensive validation result with step and field breakdowns</returns>
        Task<FormConfigurationValidationResult> ValidateConfigurationAsync(
            FormConfiguration config,
            IMetadataService metadataService);
    }

    public class FormTemplateValidationService : IFormTemplateValidationService
    {
        /// <summary>
        /// Validates a single form field against entity metadata.
        /// 
        /// Validation steps:
        /// 1. Check if entity metadata exists
        /// 2. Check if the field name exists on the entity (case-insensitive match)
        /// 3. Validate type compatibility (String, Integer, Boolean, Object, List, etc.)
        /// 4. Validate nullability constraints (non-nullable fields must be required)
        /// 
        /// This method is critical for preventing runtime errors when form data is bound to entity models.
        /// For inherited entities (e.g., Town extends Domain), the metadata includes parent class fields.
        /// </summary>
        public TemplateFieldValidationResult ValidateField(FormField field, EntityMetadataDto? entityMetadata)
        {
            var result = new TemplateFieldValidationResult
            {
                FormFieldId = field.Id,
                FieldName = field.FieldName,
                IsCompatible = true,
                Issues = new List<string>()
            };

            // Step 1: Verify metadata is available
            if (entityMetadata == null)
            {
                result.IsCompatible = false;
                result.Issues.Add("Entity metadata not found.");
                return result;
            }

            // Step 2: Find the field in entity metadata (includes parent class fields)
            // Case-insensitive match allows for slight naming variations
            var metadataField = entityMetadata.Fields?.FirstOrDefault(f =>
                f.FieldName.Equals(field.FieldName, StringComparison.OrdinalIgnoreCase));

            if (metadataField == null)
            {
                result.IsCompatible = false;
                result.Issues.Add($"Field '{field.FieldName}' does not exist on entity '{entityMetadata.EntityName}'.");
                return result;
            }

            // Step 3: Validate type compatibility based on field type
            // Handles simple types (String, Int), complex types (Object, List), and special types (Enum)
            ValidateFieldTypeCompatibility(field, metadataField, result);

            // Step 4: Validate nullability constraints
            // Non-nullable entity fields should ideally be marked as required in the form.
            // However, if the field has a default value in the model class, making it optional in the form is acceptable.
            // This allows forms to skip fields that will be auto-populated.
            if (!metadataField.IsNullable && !field.Required && !metadataField.HasDefaultValue)
            {
                // Critical issue: field is non-nullable, not required in form, and has no default value
                // This would cause runtime errors when trying to save incomplete data
                result.Issues.Add($"Field '{field.FieldName}' is non-nullable on entity (no default value) but marked as not required in form. Either make it required or add a default value to the model.");
            }
            else if (!metadataField.IsNullable && !field.Required && metadataField.HasDefaultValue)
            {
                // This is valid - field has a default value, so it can be optional in the form
                // The default will be applied when the entity is saved
            }

            // Mark as incompatible if any validation issues were found
            if (result.Issues.Count > 0)
            {
                result.IsCompatible = false;
            }

            return result;
        }

        /// <summary>
        /// Validates all fields within a form step against entity metadata.
        /// 
        /// A step is considered incompatible if ANY of its fields fail validation.
        /// This ensures that every part of a multi-step form is valid before accepting the configuration.
        /// 
        /// The validation checks each field independently and collects all issues,
        /// allowing developers to see all problems at once rather than fixing them one by one.
        /// </summary>
        public TemplateStepValidationResult ValidateStep(FormStep step, EntityMetadataDto? entityMetadata)
        {
            var result = new TemplateStepValidationResult
            {
                FormStepId = step.Id,
                StepName = step.StepName,
                IsCompatible = true,
                FieldResults = new List<TemplateFieldValidationResult>()
            };

            // Early return if metadata is missing
            if (entityMetadata == null)
            {
                result.IsCompatible = false;
                return result;
            }

            // Validate each field in the step and aggregate results
            // Continue checking all fields even if some fail, to provide complete feedback
            foreach (var field in step.Fields)
            {
                var fieldResult = ValidateField(field, entityMetadata);
                result.FieldResults.Add(fieldResult);

                // Mark step as incompatible if any field is invalid
                if (!fieldResult.IsCompatible)
                {
                    result.IsCompatible = false;
                }
            }

            return result;
        }

        /// <summary>
        /// Validates an entire form configuration against entity metadata.
        /// 
        /// This is the top-level validation method called before creating or updating a form configuration.
        /// It ensures that:
        /// 1. The target entity type exists and has metadata
        /// 2. All steps in the configuration are valid
        /// 3. All fields in all steps match the entity's schema
        /// 
        /// For inherited entities (e.g., Town extends Domain):
        /// - Metadata includes ALL fields from parent classes
        /// - Fields like "Name", "Description" from Domain are validated for Town
        /// - This prevents "field does not exist" errors for inherited properties
        /// 
        /// Returns a comprehensive result with:
        /// - Overall validity status
        /// - Per-step validation results
        /// - Per-field validation results with specific issues
        /// - Summary message with incompatible field count
        /// </summary>
        public async Task<FormConfigurationValidationResult> ValidateConfigurationAsync(
            FormConfiguration config,
            IMetadataService metadataService)
        {
            return await Task.Run(() =>
            {
                var result = new FormConfigurationValidationResult
                {
                    FormConfigurationId = config.Id,
                    EntityTypeName = config.EntityTypeName,
                    IsValid = true,
                    StepResults = new List<TemplateStepValidationResult>()
                };

                // Retrieve entity metadata (includes parent class fields for inherited entities)
                var entityMetadata = metadataService.GetEntityMetadata(config.EntityTypeName);
                if (entityMetadata == null)
                {
                    result.IsValid = false;
                    result.Summary = $"Entity metadata not found for '{config.EntityTypeName}'.";
                    return result;
                }

                // Validate each step in the configuration
                // Continue through all steps to collect complete validation feedback
                foreach (var step in config.Steps)
                {
                    var stepResult = ValidateStep(step, entityMetadata);
                    result.StepResults.Add(stepResult);

                    // Mark configuration as invalid if any step fails
                    if (!stepResult.IsCompatible)
                    {
                        result.IsValid = false;
                    }
                }

                // Generate detailed summary showing exactly which fields are incompatible and why
                var incompatibleFields = result.StepResults
                    .SelectMany(s => s.FieldResults)
                    .Where(f => !f.IsCompatible)
                    .ToList();

                var conditionIssues = ValidateDisplayConditions(config);

                if (incompatibleFields.Count > 0)
                {
                    // Build a detailed message listing each incompatible field with its specific issues
                    var details = string.Join("; ", incompatibleFields.Select(f => 
                        $"'{f.FieldName}' ({string.Join(", ", f.Issues)})"));
                    
                    result.Summary = $"FormConfiguration has {incompatibleFields.Count} incompatible field(s) for entity '{config.EntityTypeName}': {details}";
                }
                else
                {
                    result.Summary = "All fields are compatible with entity metadata.";
                }

                if (conditionIssues.Count > 0)
                {
                    result.IsValid = false;
                    result.Summary += $" Display condition problems: {string.Join("; ", conditionIssues)}";
                }

                return result;
            });
        }

        /// <summary>
        /// A display condition may only read a field the user has already passed, otherwise the
        /// target's visibility would depend on input that does not exist yet.
        /// </summary>
        private static List<string> ValidateDisplayConditions(FormConfiguration config)
        {
            var issues = new List<string>();

            var orderedSteps = FormOrdering.OrderSteps(config);
            var stepIndexByGuid = new Dictionary<Guid, int>();
            var fieldPositionByGuid = new Dictionary<Guid, (int Step, int Field)>();
            var fieldLabelByGuid = new Dictionary<Guid, string>();

            for (int s = 0; s < orderedSteps.Count; s++)
            {
                stepIndexByGuid[orderedSteps[s].StepGuid] = s;
                var orderedFields = FormOrdering.OrderFields(orderedSteps[s]);
                for (int f = 0; f < orderedFields.Count; f++)
                {
                    fieldPositionByGuid[orderedFields[f].FieldGuid] = (s, f);
                    fieldLabelByGuid[orderedFields[f].FieldGuid] = orderedFields[f].Label ?? orderedFields[f].FieldName;
                }
            }

            foreach (var step in orderedSteps)
            {
                var targetStepIndex = stepIndexByGuid[step.StepGuid];

                foreach (var condition in step.DisplayConditionGroups.SelectMany(g => g.Conditions))
                {
                    if (!fieldPositionByGuid.TryGetValue(condition.SourceFormFieldGuid, out var source))
                    {
                        issues.Add($"step '{step.StepName}' references a source field that is not part of this configuration");
                        continue;
                    }

                    if (source.Step >= targetStepIndex)
                    {
                        issues.Add($"step '{step.StepName}' depends on '{fieldLabelByGuid[condition.SourceFormFieldGuid]}', which is not in an earlier step");
                    }
                }

                foreach (var field in step.Fields)
                {
                    if (!fieldPositionByGuid.TryGetValue(field.FieldGuid, out var target)) continue;

                    foreach (var condition in field.DisplayConditionGroups.SelectMany(g => g.Conditions))
                    {
                        if (condition.SourceFormFieldGuid == field.FieldGuid)
                        {
                            issues.Add($"field '{field.Label ?? field.FieldName}' cannot depend on itself");
                            continue;
                        }

                        if (!fieldPositionByGuid.TryGetValue(condition.SourceFormFieldGuid, out var source))
                        {
                            issues.Add($"field '{field.Label ?? field.FieldName}' references a source field that is not part of this configuration");
                            continue;
                        }

                        if (source.Step > target.Step || (source.Step == target.Step && source.Field >= target.Field))
                        {
                            issues.Add($"field '{field.Label ?? field.FieldName}' depends on '{fieldLabelByGuid[condition.SourceFormFieldGuid]}', which does not come earlier in the form");
                        }
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Validates type compatibility between a form field and its corresponding entity property.
        /// 
        /// Type validation is critical because:
        /// - Prevents runtime binding errors when form data is mapped to entities
        /// - Ensures data can be properly serialized/deserialized
        /// - Catches configuration mistakes early (e.g., treating a string as an integer)
        /// 
        /// Handles multiple field type categories:
        /// - Simple types: String, Integer, Boolean, DateTime, Decimal
        /// - Object types: Related entities (e.g., Location, Street)
        /// - List types: Collections of items
        /// - Enum types: Named constant values
        /// - Special types: Custom pickers like HybridMinecraftMaterialRefPicker
        /// </summary>
        private void ValidateFieldTypeCompatibility(
            FormField field,
            FieldMetadataDto metadataField,
            TemplateFieldValidationResult result)
        {
            switch (field.FieldType)
            {
                case FieldType.String:
                case FieldType.Integer:
                case FieldType.Boolean:
                case FieldType.DateTime:
                case FieldType.Decimal:
                    // Simple primitive types: verify exact type match
                    ValidateSimpleTypeCompatibility(field, metadataField, result);
                    break;

                case FieldType.Object:
                    // Object types represent related entities (foreign keys)
                    // Must verify it's marked as RelatedEntity and types match
                    ValidateObjectTypeCompatibility(field, metadataField, result);
                    break;

                case FieldType.Enum:
                    // Enum types: verify entity field is also an enum
                    if (!metadataField.IsEnum && !metadataField.FieldType.Equals("Enum", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Issues.Add($"Field '{field.FieldName}' is Enum in form but '{metadataField.FieldType}' on entity.");
                    }
                    break;

                case FieldType.List:
                    // List types: verify entity field is a collection type
                    ValidateListTypeCompatibility(field, metadataField, result);
                    break;

                case FieldType.HybridMinecraftMaterialRefPicker:
                    // Special application-specific type, no validation needed
                    break;

                case FieldType.HybridMinecraftEnchantmentRefPicker:
                    // Special application-specific type, no validation needed
                    break;

                case FieldType.BannerLayers:
                    // #232: an ordered layer list on a collection property (ItemBlueprint.ShieldBannerLayers).
                    ValidateListTypeCompatibility(field, metadataField, result);
                    break;

                default:
                    result.Issues.Add($"Unknown field type: {field.FieldType}");
                    break;
            }
        }

        /// <summary>
        /// Validates simple (primitive) type compatibility.
        /// 
        /// Maps form field types to C# CLR types:
        /// - String → String
        /// - Integer → Int32
        /// - Boolean → Boolean
        /// - DateTime → DateTime
        /// - Decimal → Decimal or Double (both numeric types)
        /// 
        /// Uses lenient matching to handle nullable types:
        /// - "Int32" matches both "Int32" and "Int32?"
        /// - This allows forms to work with both required and optional fields
        /// 
        /// DECIMAL vs DOUBLE vs FLOAT:
        /// - Form FieldType.Decimal accepts "Decimal" (C# decimal), "Double" (C# double), and "Float" (C# float)
        /// - This provides flexibility in model design
        /// - All three types represent numeric values suitable for form decimal inputs
        /// - Examples: Location.Yaw/Pitch (float), GateStructure.HealthMax (double), other fields (decimal)
        /// </summary>
        private void ValidateSimpleTypeCompatibility(
            FormField field,
            FieldMetadataDto metadataField,
            TemplateFieldValidationResult result)
        {
            // Map form field type to expected CLR type name(s)
            // For Decimal, we accept "Decimal", "Double", and "Float" as compatible numeric types
            var expectedTypes = field.FieldType switch
            {
                FieldType.String => new[] { "String" },
                FieldType.Integer => new[] { "Integer" },
                FieldType.Boolean => new[] { "Boolean" },
                FieldType.DateTime => new[] { "DateTime" },
                FieldType.Decimal => new[] { "Decimal", "Float" }, // Accept decimal, float (float/double both map to friendly names)
                _ => Array.Empty<string>()
            };

            // Check if the metadata field type matches any of the expected types
            var isMatch = expectedTypes.Any(expectedType =>
                metadataField.FieldType.Equals(expectedType, StringComparison.OrdinalIgnoreCase) ||
                metadataField.FieldType.Contains(expectedType, StringComparison.OrdinalIgnoreCase)
            );

            if (!isMatch)
            {
                var expectedTypeStr = expectedTypes.Length > 1 
                    ? string.Join(" or ", expectedTypes) 
                    : expectedTypes.FirstOrDefault() ?? "";
                result.Issues.Add($"Field '{field.FieldName}' type mismatch: expected '{expectedTypeStr}', got '{metadataField.FieldType}'.");
            }
        }

        /// <summary>
        /// Validates object type compatibility for related entities.
        /// 
        /// Object fields represent relationships between entities (foreign keys).
        /// For example:
        /// - Town.Location references a Location entity
        /// - FormField.ObjectType = "Location" must match entity's RelatedEntityType
        /// 
        /// Validation ensures:
        /// 1. The entity field is marked with [RelatedEntityField] attribute
        /// 2. The related entity type matches (e.g., "Location", "Street", "District")
        /// 
        /// This prevents invalid relationships like trying to assign a Street to a Location field.
        /// </summary>
        private void ValidateObjectTypeCompatibility(
            FormField field,
            FieldMetadataDto metadataField,
            TemplateFieldValidationResult result)
        {
            // Verify the entity field is marked as a related entity
            if (!metadataField.IsRelatedEntity)
            {
                result.Issues.Add($"Field '{field.FieldName}' is object-type but entity metadata does not mark it as related entity.");
                return;
            }

            // If form specifies an object type, verify it matches the entity's related type
            if (!string.IsNullOrWhiteSpace(field.ObjectType))
            {
                if (!field.ObjectType.Equals(metadataField.RelatedEntityType, StringComparison.OrdinalIgnoreCase))
                {
                    result.Issues.Add($"Field '{field.FieldName}' references '{field.ObjectType}' but entity field is related to '{metadataField.RelatedEntityType}'.");
                }
            }
        }

        /// <summary>
        /// Validates list type compatibility for collection fields.
        /// 
        /// List fields represent collections of items:
        /// - Lists of related entities (e.g., Town.Streets, Town.Districts)
        /// - Lists of primitive values (less common)
        /// 
        /// Validation is lenient because:
        /// - C# has multiple collection interfaces (ICollection, IEnumerable, List)
        /// - Related entity collections (marked with [RelatedEntityField]) are also valid
        /// 
        /// For example, Town.Streets is ICollection<Street>:
        /// - FieldType contains "Collection" → passes
        /// - IsRelatedEntity = true → also passes
        /// </summary>
        private void ValidateListTypeCompatibility(
            FormField field,
            FieldMetadataDto metadataField,
            TemplateFieldValidationResult result)
        {
            // Check if entity field is a collection type
            // Accept any of: List, Collection, IEnumerable
            if (!metadataField.FieldType.Contains("List", StringComparison.OrdinalIgnoreCase) &&
                !metadataField.FieldType.Contains("Collection", StringComparison.OrdinalIgnoreCase) &&
                !metadataField.FieldType.Contains("IEnumerable", StringComparison.OrdinalIgnoreCase))
            {
                // Also accept if it's a related entity (e.g., ICollection<Street>)
                // Related entities are often rendered as multi-select lists in forms
                if (!metadataField.IsRelatedEntity)
                {
                    result.Issues.Add($"Field '{field.FieldName}' is list-type but entity metadata does not mark it as a collection.");
                }
            }
        }
    }
}
