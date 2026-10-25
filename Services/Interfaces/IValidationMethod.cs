using System.Collections.Generic;
using System.Threading.Tasks;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Interface for validation method implementations.
    /// Each validation type (LocationInsideRegion, RegionContainment, etc.) implements this interface.
    /// </summary>
    public interface IValidationMethod
    {
        /// <summary>
        /// The unique identifier for this validation type.
        /// Must match the ValidationType property in FieldValidationRule.
        /// 
        /// EXAMPLES:
        /// - "LocationInsideRegion"
        /// - "RegionContainment"
        /// - "ConditionalRequired"
        /// </summary>
        string ValidationType { get; }

        /// <summary>
        /// KNG-269: true when the verdict depends on state outside the inputs (the region validators ask
        /// WorldGuard through the plugin), so a stored result expires after the configured TTL. A pure
        /// validator's stored result holds as long as the rule and its inputs are unchanged.
        /// </summary>
        bool ReadsExternalState => false;

        /// <summary>
        /// KNG-269: the properties of the (resolved) dependency record the verdict and message read, as
        /// dotted paths; the stored result is keyed on those values only. Null = the whole dependency
        /// value, which for a loaded entity includes its navigation collections.
        /// </summary>
        IReadOnlyCollection<string>? DependencyPropertiesRead(string? configJson) => null;
        
        /// <summary>
        /// Execute the validation logic.
        /// </summary>
        /// <param name="fieldValue">The value of the field being validated</param>
        /// <param name="dependencyValue">The value of the dependency field (if applicable)</param>
        /// <param name="configJson">Validation-specific configuration (JSON string)</param>
        /// <param name="formContextData">Additional form context data (e.g., entity IDs, parent relationships)</param>
        /// <returns>Validation result with success/failure, message, and placeholders for interpolation</returns>
        Task<ValidationMethodResult> ValidateAsync(
            object? fieldValue,
            object? dependencyValue,
            string? configJson,
            Dictionary<string, object>? formContextData
        );
    }
    
    /// <summary>
    /// Result returned by validation method implementations.
    /// </summary>
    public class ValidationMethodResult
    {
        /// <summary>
        /// True if validation passed, false otherwise.
        /// </summary>
        public bool IsValid { get; set; }
        
        /// <summary>
        /// Message to display to the user.
        /// Can contain placeholders (e.g., {townName}, {coordinates}) for frontend interpolation.
        /// </summary>
        public string Message { get; set; } = string.Empty;
        
        /// <summary>
        /// Key-value pairs for message placeholder interpolation.
        /// Frontend replaces {key} in Message with corresponding value.
        /// 
        /// EXAMPLE:
        /// Message = "Location {coordinates} is outside {townName}'s boundaries."
        /// Placeholders = { {"coordinates", "1234, 5678"}, {"townName", "Spawn City"} }
        /// Result = "Location 1234, 5678 is outside Spawn City's boundaries."
        /// </summary>
        public Dictionary<string, string>? Placeholders { get; set; }
        
        /// <summary>
        /// Additional metadata about the validation execution.
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
