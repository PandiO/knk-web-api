using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.Interfaces;

/// <summary>
/// Service interface for resolving placeholder variables in validation error/success messages.
/// 
/// RESOLUTION LAYERS:
/// - Layer 0: Direct properties from current form data (extracted by frontend)
/// - Layer 1: Single-level navigation properties (one DB query with Include)
/// - Layer 2: Multi-level navigation chains (dynamic Include chains)
/// - Layer 3: Aggregate operations on collections (Count, First, Last, etc.)
/// 
/// DESIGN PRINCIPLES:
/// - Backend prepares placeholder values but does NOT interpolate
/// - Frontend/Plugin perform final string replacement
/// - Fail-open design: resolution errors don't block validation
/// - Single roundtrip: all DB queries optimized into one call
/// </summary>
public interface IPlaceholderResolutionService
{
    /// <summary>
    /// Extract all placeholder paths from a message template.
    /// 
    /// PATTERN: Regex match for {PlaceholderPath}
    /// 
    /// EXAMPLES:
    /// - "Location is outside {Town.Name}'s boundaries." → ["Town.Name"]
    /// - "Found {violationCount} issues in {regionName}." → ["violationCount", "regionName"]
    /// 
    /// </summary>
    /// <param name="message">The message template containing placeholders</param>
    /// <returns>List of placeholder paths (without braces)</returns>
    Task<List<string>> ExtractPlaceholdersAsync(string message);

    /// <summary>
    /// Resolve all placeholder layers in a single operation.
    /// 
    /// FLOW:
    /// 1. Extract placeholders from request (or from rule's ErrorMessage/SuccessMessage)
    /// 2. Categorize by layer using PlaceholderPath.Parse()
    /// 3. Resolve Layer 0 (return currentEntityPlaceholders as-is)
    /// 4. Resolve Layer 1 (single navigation with Include)
    /// 5. Resolve Layer 2 (multi-level navigation with dynamic Include chains)
    /// 6. Resolve Layer 3 (aggregate operations on collections)
    /// 7. Merge all resolved values into single dictionary
    /// 8. Collect resolution errors for unresolved placeholders
    /// 9. Return PlaceholderResolutionResponse
    /// 
    /// DATABASE OPTIMIZATION:
    /// All database queries are optimized into a single roundtrip using strategically
    /// placed Include() paths. No N+1 queries.
    /// 
    /// </summary>
    /// <param name="request">The placeholder resolution request</param>
    /// <returns>Response containing resolved placeholders and any errors</returns>
    Task<PlaceholderResolutionResponse> ResolveAllLayersAsync(PlaceholderResolutionRequest request);

    /// <summary>
    /// Resolve Layer 0 placeholders (direct form field values).
    /// 
    /// SOURCE: currentEntityPlaceholders from request
    /// NO DATABASE QUERIES: Simply returns the dictionary as-is
    /// 
    /// VALIDATION:
    /// - Keys must not contain dots (navigation is Layer 1+)
    /// - Values are already stringified by frontend
    /// 
    /// </summary>
    /// <param name="currentEntityPlaceholders">Dictionary of field names to values</param>
    /// <returns>Dictionary of resolved Layer 0 placeholders</returns>
    Task<Dictionary<string, string>> ResolveLayer0Async(Dictionary<string, string>? currentEntityPlaceholders);

    /// <summary>
    /// Resolve Layer 1 placeholders (single-level navigation properties).
    /// 
    /// PATTERN: {NavigationProperty.PropertyName}
    /// EXAMPLE: {Town.Name} → Fetch Town entity, extract Name property
    /// 
    /// ALGORITHM:
    /// 1. Group placeholders by navigation property (e.g., "Town" in "Town.Name")
    /// 2. For each navigation group:
    ///    a. Get entity type's navigation property metadata via reflection
    ///    b. Get foreign key value from entity (e.g., District.TownId)
    ///    c. Fetch related entity using dbContext.Set&lt;T&gt;().FindAsync(foreignKeyValue)
    ///    d. Extract requested properties from related entity
    /// 3. Return dictionary with resolved values
    /// 
    /// ERROR HANDLING:
    /// - Foreign key not set → Add to ResolutionErrors with code "DependencyNotFilled"
    /// - Navigation property not found → "InvalidPath"
    /// - Related entity not found → "EntityNotFound"
    /// - Property extraction failed → "NavigationFailed"
    /// 
    /// </summary>
    /// <param name="entityType">The type of the entity being validated</param>
    /// <param name="entityId">The ID of the entity instance</param>
    /// <param name="singleNavPlaceholders">List of Layer 1 placeholder paths</param>
    /// <returns>Dictionary of resolved placeholders</returns>
    Task<Dictionary<string, string>> ResolveLayer1Async(
        Type entityType,
        object entityId,
        List<string> singleNavPlaceholders);

    /// <summary>
    /// Resolve Layer 2 placeholders (multi-level navigation chains).
    /// 
    /// PATTERN: {Navigation1.Navigation2.PropertyName}
    /// EXAMPLE: {District.Town.Name} → Include("District").Include("District.Town"), then navigate
    /// 
    /// ALGORITHM:
    /// 1. Parse multi-level navigation paths (e.g., "District.Town.Name")
    /// 2. Build dynamic Include expression chains:
    ///    - ["District", "District.Town"] for "District.Town.Name"
    /// 3. Fetch entity with all required related entities in single query
    /// 4. Navigate property chain using reflection
    /// 5. Extract final property value
    /// 6. Return dictionary with resolved values
    /// 
    /// OPTIMIZATION:
    /// All include paths are combined into one query to avoid N+1:
    /// dbContext.Set&lt;T&gt;()
    ///   .Include("District")
    ///   .Include("District.Town")
    ///   .Include("Structure")
    ///   .Include("Structure.Location")
    ///   .FirstOrDefaultAsync(e => e.Id == entityId)
    /// 
    /// ERROR HANDLING:
    /// - Navigation chain broken (null intermediate) → "NavigationFailed"
    /// - Invalid path structure → "InvalidPath"
    /// 
    /// </summary>
    /// <param name="entityType">The type of the entity being validated</param>
    /// <param name="entityId">The ID of the entity instance</param>
    /// <param name="multiNavPlaceholders">List of Layer 2 placeholder paths</param>
    /// <returns>Dictionary of resolved placeholders</returns>
    Task<Dictionary<string, string>> ResolveLayer2Async(
        Type entityType,
        object entityId,
        List<string> multiNavPlaceholders);

    /// <summary>
    /// Resolve Layer 3 placeholders (aggregate operations on collections).
    /// 
    /// PATTERN: {Navigation.Collection.AggregateOp}
    /// EXAMPLE: {Town.Districts.Count} → Load Town with Districts, count collection
    /// 
    /// SUPPORTED AGGREGATES:
    /// - Count: Collection.Count()
    /// - First: Collection.First().ToString()
    /// - Last: Collection.Last().ToString()
    /// - Any: Collection.Any() ? "true" : "false"
    /// 
    /// ALGORITHM:
    /// 1. Parse aggregate operations from placeholder paths
    /// 2. Navigate to collection property
    /// 3. Execute aggregate operation:
    ///    - Count → Cast to IEnumerable, call Count()
    ///    - First → Get first element's ToString()
    ///    - Last → Get last element's ToString()
    ///    - Any → Check if collection has any elements
    /// 4. Return dictionary with resolved values (as strings)
    /// 
    /// ERROR HANDLING:
    /// - Collection property not found → "InvalidPath"
    /// - Collection is null → "NavigationFailed"
    /// - Collection empty (for First/Last) → "AggregateEmpty"
    /// 
    /// </summary>
    /// <param name="entityType">The type of the entity being validated</param>
    /// <param name="entityId">The ID of the entity instance</param>
    /// <param name="aggregatePlaceholders">List of Layer 3 placeholder paths</param>
    /// <returns>Dictionary of resolved placeholders</returns>
    Task<Dictionary<string, string>> ResolveLayer3Async(
        Type entityType,
        object entityId,
        List<string> aggregatePlaceholders);

    /// <summary>
    /// Utility: Interpolate placeholders in a message template.
    /// 
    /// NOTE: This is a server-side utility for logging/debugging.
    /// Frontend should use its own interpolation utility for actual display.
    /// 
    /// ALGORITHM:
    /// 1. For each key-value pair in placeholders dictionary
    /// 2. Replace all occurrences of {key} with value
    /// 3. Use case-insensitive replacement
    /// 4. Return interpolated message
    /// 
    /// EXAMPLES:
    /// - Template: "Location is outside {Town.Name}'s boundaries."
    /// - Placeholders: { "Town.Name": "Springfield" }
    /// - Result: "Location is outside Springfield's boundaries."
    /// 
    /// </summary>
    /// <param name="messageTemplate">The template containing {placeholders}</param>
    /// <param name="placeholders">Dictionary of placeholder names to values</param>
    /// <returns>Interpolated message string</returns>
    string InterpolatePlaceholders(
        string messageTemplate,
        Dictionary<string, string> placeholders);

    /// <summary>
    /// Load a saved entity by its type name (a FormField.ObjectType, e.g. "Town") and id.
    /// Returns null when the type is unknown or no row has that id.
    /// </summary>
    Task<object?> LoadEntityByIdAsync(string entityTypeName, object id);
}
