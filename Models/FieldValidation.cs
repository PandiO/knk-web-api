using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models
{
    /// <summary>
    /// Represents a single validation rule for a FormField.
    /// Multiple validations can be applied to one field (e.g., Required + CharacterLength + Regex).
    /// 
    /// WHY JSON for Parameters instead of dedicated columns?
    /// 1. DIFFERENT VALIDATION TYPES NEED DIFFERENT PARAMETERS:
    ///    - CharacterLength needs: { "Min": 1, "Max": 255 }
    ///    - Range needs: { "Min": 0, "Max": 100 }
    ///    - Regex needs: { "Pattern": "^[A-Za-z]+$" }
    ///    - Email needs: no parameters (just the validation type)
    /// 2. EXTENSIBILITY: New validation types can be added without altering the table schema.
    /// 3. FLEXIBILITY: Combine multiple parameter types in one JSON object.
    /// 
    /// Both frontend and backend interpret these validation rules.
    /// Frontend uses them for instant feedback (UX).
    /// Backend enforces them for security (never trust client-side validation alone).
    /// </summary>
    public class FieldValidation
    {
        public int Id { get; set; }
        
        /// <summary>
        /// The type of validation to perform.
        /// Examples: Required, CharacterLength, MinValue, MaxValue, Range, Email, Url, Regex, Custom.
        /// </summary>
        public ValidationType Type { get; set; }
        
        /// <summary>
        /// JSON object containing validation parameters specific to the validation type.
        /// 
        /// EXAMPLES:
        /// Required: {} (no parameters needed)
        /// CharacterLength: { "Min": 3, "Max": 50 }
        /// MinValue: { "Min": 0 }
        /// MaxValue: { "Max": 100 }
        /// Range: { "Min": 1, "Max": 10 }
        /// Regex: { "Pattern": "^[0-9]{5}$", "Flags": "i" }
        /// Email: {} (no parameters, just validates email format)
        /// Custom: { "FunctionName": "validateUniqueUsername", "Endpoint": "/api/validate/username" }
        /// 
        /// Frontend parses this JSON and applies the appropriate validation logic.
        /// Backend does the same for server-side validation before saving data.
        /// </summary>
        public string ParametersJson { get; set; } = "{}";
        
        /// <summary>
        /// Custom error message to display when validation fails.
        /// If NULL, a default message is generated based on the validation type.
        /// Examples:
        /// - "Shop name must be between 3 and 50 characters."
        /// - "Please enter a valid email address."
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// False switches the validation off without deleting it; the wizard then skips it. Until
        /// 2026-10-23 (KNG-186) this existed on the DTO only and the API always sent false. Existing
        /// rows are active.
        /// </summary>
        public bool IsActive { get; set; } = true;
        
        // Foreign key to FormField
        public int FormFieldId { get; set; }
        public FormField FormField { get; set; } = null!;
    }
}
