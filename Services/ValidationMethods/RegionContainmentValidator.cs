using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services.ValidationMethods
{
    /// <summary>
    /// Validates that a child region is fully contained within a parent region.
    /// 
    /// ConfigJson schema:
    /// {
    ///   "parentRegionPath": "WgRegionId",     // Path to parent region property on dependency entity
    ///   "requireFullContainment": true         // Whether child must be fully inside parent
    /// }
    /// 
    /// Example: District region must be inside Town region
    /// - FieldValue: District object (with WgRegionId - the child region)
    /// - DependencyValue: Town object (with WgRegionId - the parent region)
    /// - ConfigJson: { "parentRegionPath": "WgRegionId", "requireFullContainment": true }
    /// </summary>
    public class RegionContainmentValidator : IValidationMethod
    {
        private readonly IRegionService _regionService;

        public RegionContainmentValidator(IRegionService regionService)
        {
            _regionService = regionService;
        }

        public string ValidationType => "RegionContainment";

        public async Task<ValidationMethodResult> ValidateAsync(
            object? fieldValue,
            object? dependencyValue,
            string? configJson,
            Dictionary<string, object>? formContextData)
        {
            try
            {
                Console.WriteLine($"[REGIONCONTAINMENT] ValidateAsync started");
                Console.WriteLine($"[REGIONCONTAINMENT]   fieldValue type: {fieldValue?.GetType().Name ?? "null"}");
                Console.WriteLine($"[REGIONCONTAINMENT]   fieldValue: {fieldValue ?? "null"}");
                Console.WriteLine($"[REGIONCONTAINMENT]   dependencyValue type: {dependencyValue?.GetType().Name ?? "null"}");
                
                // CRITICAL FIX: dependencyValue arrives as JsonElement from API controller
                // Need to deserialize it to a proper object for property reflection
                if (dependencyValue != null && dependencyValue is System.Text.Json.JsonElement jsonElement)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   dependencyValue is JsonElement, raw JSON: {jsonElement.GetRawText()}");
                    // Deserialize to dynamic object for property access
                    var jsonString = jsonElement.GetRawText();
                    dependencyValue = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
                    Console.WriteLine($"[REGIONCONTAINMENT]   dependencyValue deserialized to Dictionary");
                }
                else if (dependencyValue != null)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   dependencyValue: {dependencyValue}");
                }
                
                Console.WriteLine($"[REGIONCONTAINMENT]   configJson: {configJson ?? "null"}");

                // Validate inputs
                if (fieldValue == null)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   FAIL: fieldValue is null");
                    return new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = "Child region value is required",
                        Placeholders = new Dictionary<string, string>()
                    };
                }

                if (dependencyValue == null)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   FAIL: dependencyValue is null");
                    return new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = "Dependency value (parent entity) is required",
                        Placeholders = new Dictionary<string, string>()
                    };
                }

                // Parse configuration
                var config = string.IsNullOrEmpty(configJson)
                    ? new RegionContainmentConfig()
                    : JsonSerializer.Deserialize<RegionContainmentConfig>(configJson)
                        ?? new RegionContainmentConfig();
                Console.WriteLine($"[REGIONCONTAINMENT]   config.ParentRegionPath: {config.ParentRegionPath}");
                Console.WriteLine($"[REGIONCONTAINMENT]   config.RequireFullContainment: {config.RequireFullContainment}");

                // Extract child region ID
                var childRegionId = ExtractRegionId(fieldValue);
                Console.WriteLine($"[REGIONCONTAINMENT]   extracted childRegionId: {childRegionId ?? "null"}");
                if (childRegionId == null)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   FAIL: Cannot extract childRegionId from fieldValue");
                    return new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = "Invalid or missing child region value",
                        Placeholders = new Dictionary<string, string>()
                    };
                }

                // Extract parent region ID from dependency value
                var parentRegionId = ExtractPropertyValue(dependencyValue, config.ParentRegionPath);
                Console.WriteLine($"[REGIONCONTAINMENT]   extracted parentRegionId: {parentRegionId ?? "null"}");
                if (parentRegionId == null)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   FAIL: Cannot extract parentRegionId using path '{config.ParentRegionPath}'");
                    return new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = $"Cannot extract parent region from dependency entity using path '{config.ParentRegionPath}'",
                        Placeholders = new Dictionary<string, string>()
                    };
                }

                // Check if child region is fully contained in parent region
                Console.WriteLine($"[REGIONCONTAINMENT]   calling IsRegionContainedAsync with parentRegionId={parentRegionId}, childRegionId={childRegionId}, requireFull={config.RequireFullContainment}");
                bool isContained;
                try
                {
                    isContained = await _regionService.IsRegionContainedAsync(
                        parentRegionId.ToString(),
                        childRegionId.ToString(),
                        config.RequireFullContainment);
                }
                catch (RegionServiceUnavailableException ex)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   Region service unavailable: {ex.Message}");
                    return new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = "Cannot verify region containment right now: the Minecraft server or knk-plugin is not running or unreachable. Start the server and plugin, then use \"Re-run validation\".",
                        Metadata = new Dictionary<string, object> { { "failureReason", "PluginUnreachable" } },
                        Placeholders = new Dictionary<string, string>()
                    };
                }
                Console.WriteLine($"[REGIONCONTAINMENT]   IsRegionContainedAsync returned: {isContained}");

                if (isContained)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   PASS: Region is contained");
                    return new ValidationMethodResult
                    {
                        IsValid = true,
                        Message = "Child region is fully contained within the parent region"
                    };
                }

                // Get parent entity name for error message
                var parentEntityName = ExtractPropertyValue(dependencyValue, "Name")?.ToString() ?? "Parent Region";
                Console.WriteLine($"[REGIONCONTAINMENT]   FAIL: Region not contained. parentEntityName={parentEntityName}");

                return new ValidationMethodResult
                {
                    IsValid = false,
                    Message = $"The region extends outside {parentEntityName}'s boundaries. All boundary points must be within the parent region.",
                    Placeholders = new Dictionary<string, string>
                    {
                        { "townName", parentEntityName },
                        { "parentEntityName", parentEntityName },
                        { "childRegionId", childRegionId.ToString() },
                        { "parentRegionId", parentRegionId.ToString() }
                    }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[REGIONCONTAINMENT]   EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[REGIONCONTAINMENT]   Stack trace: {ex.StackTrace}");
                return new ValidationMethodResult
                {
                    IsValid = false,
                    Message = $"Validation error: {ex.Message}",
                    Placeholders = new Dictionary<string, string>()
                };
            }
        }

        /// <summary>
        /// Extract region ID from a field value (can be an entity with region id, ID, or string).
        /// </summary>
        private object? ExtractRegionId(object fieldValue)
        {
            if (fieldValue == null)
            {
                Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: fieldValue is null");
                return null;
            }

            // Handle JsonElement from API deserialization
            if (fieldValue is System.Text.Json.JsonElement jsonElem)
            {
                Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: fieldValue is JsonElement, ValueKind={jsonElem.ValueKind}");
                
                // If it's a string, return the string value
                if (jsonElem.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var strValue = jsonElem.GetString();
                    Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: returning JsonElement string value '{strValue}'");
                    return strValue;
                }
                
                // If it's a number, return as int
                if (jsonElem.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    var intValue = jsonElem.GetInt32();
                    Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: returning JsonElement int value {intValue}");
                    return intValue;
                }
                
                // If it's an object, try to extract WgRegionId property
                if (jsonElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: JsonElement is object, attempting to extract WgRegionId property");
                    if (jsonElem.TryGetProperty("WgRegionId", out var regionIdProp) || 
                        jsonElem.TryGetProperty("wgRegionId", out regionIdProp))
                    {
                        var value = regionIdProp.ValueKind == System.Text.Json.JsonValueKind.String 
                            ? regionIdProp.GetString() 
                            : (object?)regionIdProp.GetInt32();
                        Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: found WgRegionId property, value={value ?? "null"}");
                        return value;
                    }
                    Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: WgRegionId property not found in JsonElement object");
                    return null;
                }
                
                Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: JsonElement ValueKind={jsonElem.ValueKind} not handled");
                return null;
            }

            // If it's a region ID integer
            if (fieldValue is int regionId)
            {
                Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: returning int value {regionId}");
                return regionId;
            }

            // If it's a region ID string
            if (fieldValue is string regionIdStr)
            {
                Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: returning string value '{regionIdStr}'");
                return regionIdStr;
            }

            // Try to extract WgRegionId property (for entity objects)
            Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: fieldValue is {fieldValue.GetType().Name}, attempting to extract WgRegionId property");
            var property = fieldValue.GetType().GetProperty("WgRegionId",
                System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public);
            if (property != null)
            {
                var value = property.GetValue(fieldValue);
                Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: found WgRegionId property, value={value ?? "null"}");
                return value;
            }

            Console.WriteLine($"[REGIONCONTAINMENT]   ExtractRegionId: WgRegionId property not found on {fieldValue.GetType().Name}");
            return null;
        }

        /// <summary>
        /// Extract a property value from an object using dot notation path.
        /// Example: "Town.WgRegionId" or "WgRegionId"
        /// </summary>
        private object? ExtractPropertyValue(object obj, string path)
        {
            Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: path='{path}', obj type={obj?.GetType().Name ?? "null"}");
            
            if (obj == null || string.IsNullOrEmpty(path))
            {
                Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: obj={obj ?? "null"}, path={path ?? "null"}, returning null");
                return null;
            }

            var parts = path.Split('.');
            var current = obj;

            foreach (var part in parts)
            {
                if (current == null)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: current became null while processing part '{part}'");
                    return null;
                }

                Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: extracting '{part}' from {current.GetType().Name}");
                
                // Handle JsonElement (from API deserialization)
                if (current is System.Text.Json.JsonElement jsonElem)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: current is JsonElement, attempting GetProperty('{part}')");
                    
                    // Try exact match first, then case-insensitive match
                    if (!jsonElem.TryGetProperty(part, out var propValue))
                    {
                        // Try case-insensitive lookup
                        var properties = jsonElem.EnumerateObject();
                        var matchedProp = properties.FirstOrDefault(p => string.Equals(p.Name, part, StringComparison.OrdinalIgnoreCase));
                        if (matchedProp.Name != null)
                        {
                            propValue = matchedProp.Value;
                            Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: found property '{part}' (actual: '{matchedProp.Name}') in JsonElement (case-insensitive)");
                        }
                        else
                        {
                            Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: property '{part}' not found in JsonElement");
                            return null;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: found property '{part}' in JsonElement");
                    }

                    // Get the actual value based on JsonElement type
                    current = propValue.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.String => propValue.GetString(),
                        System.Text.Json.JsonValueKind.Number => propValue.GetInt32(),
                        System.Text.Json.JsonValueKind.True => true,
                        System.Text.Json.JsonValueKind.False => false,
                        System.Text.Json.JsonValueKind.Null => null,
                        _ => propValue
                    };
                    Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: extracted value = {current?.ToString() ?? "null"}");
                    continue;
                }
                
                // Handle Dictionary (from deserialized JSON)
                if (current is Dictionary<string, object> dict)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: current is Dictionary, attempting to get key '{part}'");
                    
                    // Try case-insensitive lookup for JSON deserialized dictionaries
                    // (JSON typically uses camelCase but config may use PascalCase)
                    var key = dict.Keys.FirstOrDefault(k => string.Equals(k, part, StringComparison.OrdinalIgnoreCase));
                    if (key != null && dict.TryGetValue(key, out var dictValue))
                    {
                        Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: found key '{part}' (actual: '{key}') in Dictionary, value={dictValue?.ToString() ?? "null"}");
                        current = dictValue;
                        continue;
                    }
                    else
                    {
                        Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: key '{part}' not found in Dictionary (case-insensitive)");
                        Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: available keys: {string.Join(", ", dict.Keys)}");
                        return null;
                    }
                }

                // Handle regular objects via reflection
                var property = current.GetType().GetProperty(part,
                    System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public);
                
                if (property == null)
                {
                    Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: property '{part}' not found on {current.GetType().Name}");
                    Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: available properties: {string.Join(", ", current.GetType().GetProperties().Select(p => p.Name))}");
                    return null;
                }

                var value = property.GetValue(current);
                Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: extracted '{part}' = {value?.ToString() ?? "null"}");
                current = value;
            }

            Console.WriteLine($"[REGIONCONTAINMENT]   ExtractPropertyValue: returning {current?.ToString() ?? "null"}");
            return current;
        }
    }

    /// <summary>
    /// Configuration for RegionContainmentValidator
    /// </summary>
    public class RegionContainmentConfig
    {
        /// <summary>
        /// Property path to extract parent region ID from dependency entity.
        /// Examples: "WgRegionId", "Town.WgRegionId"
        /// </summary>
        public string ParentRegionPath { get; set; } = "WgRegionId";

        /// <summary>
        /// If true, child region must be fully contained within parent.
        /// If false, allows partial overlap.
        /// </summary>
        public bool RequireFullContainment { get; set; } = true;
    }
}
