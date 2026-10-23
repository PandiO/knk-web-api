using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Service interface for field validation operations.
    /// Orchestrates validation rule execution and result formatting.
    /// CRUD operations for validation rules are handled by IFieldValidationRuleService.
    /// </summary>
    public interface IValidationService
    {
        // Validation Execution
        /// <summary>
        /// Validate a field value against its configured validation rules using a request DTO.
        /// </summary>
        /// <param name="request">The validation request containing field ID, value, and context</param>
        /// <returns>Validation result with success/failure, messages, and metadata</returns>
        Task<ValidationResultDto> ValidateFieldAsync(ValidateFieldRequestDto request);

        /// <summary>
        /// KNG-269: as the four-argument overload, answered from the result log for <paramref name="subject"/>
        /// when the rule and its inputs are unchanged (unless <paramref name="force"/>).
        /// </summary>
        Task<ValidationResultDto> ValidateFieldAsync(
            int fieldId,
            object? fieldValue,
            object? dependencyValue,
            Dictionary<string, object>? formContextData,
            knkwebapi_v2.Services.ValidationResultLog.ValidationSubject? subject,
            bool force = false);

        /// <summary>KNG-269: every field of a step in one request; results by field id.</summary>
        Task<ValidateFieldsBatchResultDto> ValidateFieldsAsync(ValidateFieldsBatchRequestDto request);
    
        /// <summary>
        /// Validate a field value against its configured validation rules.
        /// </summary>
        /// <param name="fieldId">The form field ID to validate</param>
        /// <param name="fieldValue">The current value of the field</param>
        /// <param name="dependencyValue">The value of the dependency field (if applicable)</param>
        /// <param name="formContextData">Additional context data (entity IDs, relationships, etc.)</param>
        /// <returns>Validation result with success/failure, messages, and metadata</returns>
        Task<ValidationResultDto> ValidateFieldAsync(
            int fieldId,
            object? fieldValue,
            object? dependencyValue,
            Dictionary<string, object>? formContextData
        );
        
        /// <summary>
        /// Validate multiple fields at once (e.g., before step progression).
        /// </summary>
        /// <param name="fieldIds">Collection of field IDs to validate</param>
        /// <param name="fieldValues">Dictionary of field IDs to their current values</param>
        /// <param name="formContextData">Additional context data</param>
        /// <returns>Dictionary of field IDs to their validation results</returns>
        Task<Dictionary<int, ValidationResultDto>> ValidateMultipleFieldsAsync(
            IEnumerable<int> fieldIds,
            Dictionary<int, object?> fieldValues,
            Dictionary<string, object>? formContextData
        );
        
        /// <summary>
        /// Validate a field and return result with all placeholder values aggregated from all executed rules.
        /// This is useful when you need to interpolate messages on the frontend/plugin with all available context.
        /// </summary>
        /// <param name="fieldId">The form field ID to validate</param>
        /// <param name="fieldValue">The current value of the field</param>
        /// <param name="dependencyValue">The value of the dependency field (if applicable)</param>
        /// <param name="formContextData">Additional context data (entity IDs, relationships, etc.)</param>
        /// <returns>Validation result with aggregated placeholder data from all rules</returns>
        Task<ValidationResultDto> ValidateFieldWithPlaceholdersAsync(
            int fieldId,
            object? fieldValue,
            object? dependencyValue,
            Dictionary<string, object>? formContextData
        );
    }
}
