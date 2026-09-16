using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services.ValidationMethods
{
    /// <summary>
    /// Validates that a Location field value is inside a specified WorldGuard region.
    /// 
    /// ConfigJson schema:
    /// {
    ///   "regionPropertyPath": "WgRegionId",  // Path to region property on dependency entity
    ///   "allowBoundary": false               // Whether boundary touches are allowed
    /// }
    /// 
    /// Example: Location must be inside Town's region
    /// - FieldValue: Location object (with X, Y, Z, WorldId)
    /// - DependencyValue: Town object (with WgRegionId)
    /// - ConfigJson: { "regionPropertyPath": "WgRegionId", "allowBoundary": false }
    /// </summary>
    public class LocationInsideRegionValidator : IValidationMethod
    {
        private readonly IRegionService _regionService;
        private readonly ILocationService _locationService;

        public LocationInsideRegionValidator(IRegionService regionService, ILocationService locationService)
        {
            _regionService = regionService;
            _locationService = locationService;
        }

        public string ValidationType => "LocationInsideRegion";

        public async Task<ValidationMethodResult> ValidateAsync(
            object? fieldValue,
            object? dependencyValue,
            string? configJson,
            Dictionary<string, object>? formContextData)
        {
            Console.WriteLine("[VALIDATION_TRACE_BACKEND]     LocationInsideRegion.ValidateAsync started");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       fieldValue type: {fieldValue?.GetType().Name ?? "null"} and content: {DescribeRuntimeValue(fieldValue)} ");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       dependencyValue type: {dependencyValue?.GetType().Name ?? "null"} and content: {DescribeRuntimeValue(dependencyValue)} ");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       hasConfigJson: {!string.IsNullOrEmpty(configJson)}");
            LogMultiLine("[VALIDATION_TRACE_BACKEND]       configJson: ", configJson);
            try
            {
                // Validate inputs
                if (fieldValue == null)
                {
                    Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Validation failed: fieldValue is null");
                    return new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = "Location value is required"
                    };
                }

                if (dependencyValue == null)
                {
                    Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Validation failed: dependencyValue is null");
                    return new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = "Dependency value (parent entity) is required"
                    };
                }

                // Parse configuration
                var config = string.IsNullOrEmpty(configJson)
                    ? new LocationInsideRegionConfig()
                    : JsonSerializer.Deserialize<LocationInsideRegionConfig>(configJson) 
                        ?? new LocationInsideRegionConfig();
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Config parsed: regionPropertyPath='{config.RegionPropertyPath}', allowBoundary={config.AllowBoundary}");
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       fieldValue runtime detail: {DescribeRuntimeValue(fieldValue)}");

                // Extract Location data
                var location = fieldValue as LocationDto;
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Direct cast to LocationDto success: {location != null}");
                if (location == null)
                {
                    location = TryConvertToLocationDto(fieldValue);
                    Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Explicit conversion to LocationDto success: {location != null}");
                }

                if (location == null)
                {
                    Console.WriteLine("[VALIDATION_TRACE_BACKEND]       LocationDto cast failed; attempting fallback resolution paths");
                    Console.WriteLine("[VALIDATION_TRACE_BACKEND]       fieldValue is not LocationDto, attempting ID-based lookup");
                    // Try to extract Location ID from the field value
                    if (fieldValue is int locationId)
                    {
                        Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Resolving location by int ID: {locationId}");
                        location = await _locationService.GetByIdAsync(locationId);
                    }
                    else if (fieldValue is string locationIdStr && int.TryParse(locationIdStr, out var locId))
                    {
                        Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Resolving location by string ID: {locationIdStr}");
                        location = await _locationService.GetByIdAsync(locId);
                    }
                    else
                    {
                        Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Validation failed: unrecognized location value format");
                        return new ValidationMethodResult
                        {
                            IsValid = false,
                            Message = "Invalid location value format"
                        };
                    }

                    if (location == null)
                    {
                        Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Validation failed: location lookup returned null");
                        return new ValidationMethodResult
                        {
                            IsValid = false,
                            Message = "Location not found"
                        };
                    }
                }

                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Location resolved: X={location.X}, Y={location.Y}, Z={location.Z}");
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       dependencyValue runtime detail: {DescribeRuntimeValue(dependencyValue)}");
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       RegionPropertyPath runtime detail: {DescribeRuntimeValue(config.RegionPropertyPath)}");

                // Extract region ID from dependency value using property path
                var regionId = ExtractPropertyValue(dependencyValue, config.RegionPropertyPath);
                if (regionId == null)
                {
                    Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Validation failed: could not extract region via path '{config.RegionPropertyPath}'");
                    return new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = $"Cannot extract region from dependency entity using path '{config.RegionPropertyPath}'"
                    };
                }

                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Region resolved: {regionId}");

                // Check if location is inside region
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Checking region boundary: regionId={regionId}, x={location.X ?? 0}, z={location.Z ?? 0}, allowBoundary={config.AllowBoundary}");
                bool isInside;
                try
                {
                    isInside = await _regionService.IsLocationInsideRegionAsync(
                        regionId.ToString(),
                        location.X ?? 0,
                        location.Z ?? 0,
                        config.AllowBoundary);
                }
                catch (RegionServiceUnavailableException ex)
                {
                    Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Region service unavailable: {ex.Message}");
                    return new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = "Cannot verify the location's region boundary right now: the Minecraft server or knk-plugin is not running or unreachable. Start the server and plugin, then use \"Re-run validation\".",
                        Metadata = new Dictionary<string, object> { { "failureReason", "PluginUnreachable" } }
                    };
                }
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Region check result: isInside={isInside}");

                if (isInside)
                {
                    Console.WriteLine("[VALIDATION_TRACE_BACKEND]       Validation passed: location is inside region");
                    return new ValidationMethodResult
                    {
                        IsValid = true,
                        Message = "Location is within the allowed region"
                    };
                }

                // Get parent entity name for error message
                var parentEntityName = ExtractPropertyValue(dependencyValue, "Name")?.ToString() ?? "Region";
                var coordinates = $"{location.X}, {location.Z}";
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Validation failed: outside boundaries for parentEntity='{parentEntityName}', coordinates='{coordinates}'");

                return new ValidationMethodResult
                {
                    IsValid = false,
                    Message = $"Location {coordinates} is outside {parentEntityName}'s boundaries. Please select a location within the region.",
                    Placeholders = new Dictionary<string, string>
                    {
                        { "parentEntityName", parentEntityName },
                        { "coordinates", coordinates },
                        { "regionName", regionId.ToString() }
                    }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Exception in LocationInsideRegion.ValidateAsync: {ex.Message}");
                return new ValidationMethodResult
                {
                    IsValid = false,
                    Message = $"Validation error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Extract a property value from an object using dot notation path.
        /// Example: "Town.WgRegionId" or "WgRegionId"
        /// </summary>
        private object? ExtractPropertyValue(object obj, string path)
        {
            if (obj == null)
                return null;

            if (string.IsNullOrWhiteSpace(path))
                return obj;

            var parts = path.Split('.');
            object? current = obj;

            foreach (var part in parts)
            {
                if (current == null)
                    return null;

                if (TryGetDictionaryValue(current, part, out var dictionaryValue))
                {
                    current = dictionaryValue;
                    continue;
                }

                if (current is JsonElement jsonElement)
                {
                    if (TryGetJsonPropertyValue(jsonElement, part, out var jsonValue))
                    {
                        current = jsonValue;
                        continue;
                    }

                    if (IsTerminalValue(jsonElement))
                    {
                        return ConvertJsonElementToValue(jsonElement);
                    }

                    return null;
                }

                var property = current.GetType().GetProperty(part,
                    System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public);

                if (property == null)
                {
                    if (IsTerminalValue(current))
                    {
                        return current;
                    }

                    return null;
                }

                current = property.GetValue(current);
            }

            return current;
        }

        private static bool TryGetDictionaryValue(object current, string key, out object? value)
        {
            if (current is IDictionary<string, object> genericDictionary)
            {
                foreach (var kvp in genericDictionary)
                {
                    if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        value = kvp.Value;
                        return true;
                    }
                }
            }

            if (current is IDictionary nonGenericDictionary)
            {
                foreach (DictionaryEntry entry in nonGenericDictionary)
                {
                    if (entry.Key?.ToString()?.Equals(key, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        value = entry.Value;
                        return true;
                    }
                }
            }

            value = null;
            return false;
        }

        private static bool TryGetJsonPropertyValue(JsonElement element, string propertyName, out object? value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = null;
            return false;
        }

        private static bool IsTerminalValue(object value)
        {
            if (value is string || value is Guid || value is DateTime || value is DateTimeOffset || value is TimeSpan)
            {
                return true;
            }

            if (value is JsonElement jsonElement)
            {
                return IsTerminalValue(jsonElement);
            }

            var type = value.GetType();
            return type.IsPrimitive || type.IsEnum || value is decimal;
        }

        private static bool IsTerminalValue(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.String
                || element.ValueKind == JsonValueKind.Number
                || element.ValueKind == JsonValueKind.True
                || element.ValueKind == JsonValueKind.False
                || element.ValueKind == JsonValueKind.Null
                || element.ValueKind == JsonValueKind.Undefined;
        }

        private static object? ConvertJsonElementToValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var longValue)
                    ? longValue
                    : element.TryGetDouble(out var doubleValue)
                        ? doubleValue
                        : element.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Undefined => null,
                _ => element.GetRawText()
            };
        }

        private static LocationDto? TryConvertToLocationDto(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is LocationDto dto)
            {
                return dto;
            }

            if (value is Location model)
            {
                return new LocationDto
                {
                    Id = model.Id,
                    Name = model.Name,
                    X = model.X,
                    Y = model.Y,
                    Z = model.Z,
                    Yaw = model.Yaw,
                    Pitch = model.Pitch,
                    World = model.World
                };
            }

            if (value is JsonElement element)
            {
                try
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        var locationFromJson = JsonSerializer.Deserialize<LocationDto>(
                            element.GetRawText(),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (locationFromJson != null)
                        {
                            return locationFromJson;
                        }
                    }
                }
                catch
                {
                }
            }

            if (value is Dictionary<string, object> dict)
            {
                return CreateLocationDtoFromDictionary(dict);
            }

            var runtimeType = value.GetType();
            if (runtimeType.Name.Equals("Location", StringComparison.OrdinalIgnoreCase))
            {
                return CreateLocationDtoFromObject(value);
            }

            return null;
        }

        private static LocationDto? CreateLocationDtoFromDictionary(Dictionary<string, object> data)
        {
            if (!TryGetDouble(data, "x", out var x) ||
                !TryGetDouble(data, "y", out var y) ||
                !TryGetDouble(data, "z", out var z))
            {
                return null;
            }

            var location = new LocationDto
            {
                X = x,
                Y = y,
                Z = z
            };

            if (TryGetInt(data, "id", out var id)) location.Id = id;
            if (TryGetString(data, "name", out var name)) location.Name = name;
            if (TryGetFloat(data, "yaw", out var yaw)) location.Yaw = yaw;
            if (TryGetFloat(data, "pitch", out var pitch)) location.Pitch = pitch;
            if (TryGetString(data, "world", out var world)) location.World = world;

            return location;
        }

        private static LocationDto? CreateLocationDtoFromObject(object value)
        {
            var type = value.GetType();
            var x = GetPropertyDouble(type, value, "X");
            var y = GetPropertyDouble(type, value, "Y");
            var z = GetPropertyDouble(type, value, "Z");

            if (!x.HasValue || !y.HasValue || !z.HasValue)
            {
                return null;
            }

            return new LocationDto
            {
                Id = GetPropertyInt(type, value, "Id"),
                Name = GetPropertyString(type, value, "Name"),
                X = x.Value,
                Y = y.Value,
                Z = z.Value,
                Yaw = GetPropertyFloat(type, value, "Yaw"),
                Pitch = GetPropertyFloat(type, value, "Pitch"),
                World = GetPropertyString(type, value, "World")
            };
        }

        private static bool TryGetInt(Dictionary<string, object> data, string key, out int value)
        {
            value = 0;
            var raw = GetDictionaryValueCaseInsensitive(data, key);
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToInt32(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetDouble(Dictionary<string, object> data, string key, out double value)
        {
            value = 0;
            var raw = GetDictionaryValueCaseInsensitive(data, key);
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToDouble(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetFloat(Dictionary<string, object> data, string key, out float value)
        {
            value = 0;
            var raw = GetDictionaryValueCaseInsensitive(data, key);
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToSingle(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetString(Dictionary<string, object> data, string key, out string? value)
        {
            value = null;
            var raw = GetDictionaryValueCaseInsensitive(data, key);
            if (raw == null)
            {
                return false;
            }

            value = raw.ToString();
            return true;
        }

        private static object? GetDictionaryValueCaseInsensitive(Dictionary<string, object> data, string key)
        {
            foreach (var kvp in data)
            {
                if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private static int? GetPropertyInt(Type type, object value, string propertyName)
        {
            var raw = GetPropertyValue(type, value, propertyName);
            if (raw == null)
            {
                return null;
            }

            try
            {
                return Convert.ToInt32(raw);
            }
            catch
            {
                return null;
            }
        }

        private static double? GetPropertyDouble(Type type, object value, string propertyName)
        {
            var raw = GetPropertyValue(type, value, propertyName);
            if (raw == null)
            {
                return null;
            }

            try
            {
                return Convert.ToDouble(raw);
            }
            catch
            {
                return null;
            }
        }

        private static float? GetPropertyFloat(Type type, object value, string propertyName)
        {
            var raw = GetPropertyValue(type, value, propertyName);
            if (raw == null)
            {
                return null;
            }

            try
            {
                return Convert.ToSingle(raw);
            }
            catch
            {
                return null;
            }
        }

        private static string? GetPropertyString(Type type, object value, string propertyName)
        {
            var raw = GetPropertyValue(type, value, propertyName);
            return raw?.ToString();
        }

        private static object? GetPropertyValue(Type type, object value, string propertyName)
        {
            var property = type.GetProperty(propertyName,
                System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            return property?.GetValue(value);
        }

        private static void LogMultiLine(string prefix, string? content)
        {
            if (string.IsNullOrEmpty(content))
            {
                Console.WriteLine(prefix + "<null>");
                return;
            }

            var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                Console.WriteLine(prefix + line);
            }
        }

        private static string DescribeRuntimeValue(object? value)
        {
            if (value == null)
            {
                return "<null>";
            }

            var type = value.GetType();
            var summary = $"Type={type.FullName}, Assembly={type.Assembly.GetName().Name}";

            if (value is JsonElement element)
            {
                var raw = element.GetRawText();
                if (raw.Length > 300)
                {
                    raw = raw.Substring(0, 300) + "...";
                }

                return $"{summary}, JsonValueKind={element.ValueKind}, Raw={raw}";
            }

            if (value is string str)
            {
                return $"{summary}, StringValue='{str}'";
            }

            var properties = new List<string>();
            var propertyInfos = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var count = 0;

            foreach (var property in propertyInfos)
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                try
                {
                    var propertyValue = property.GetValue(value);
                    var propertyValueString = propertyValue?.ToString() ?? "null";
                    if (propertyValueString.Length > 80)
                    {
                        propertyValueString = propertyValueString.Substring(0, 80) + "...";
                    }

                    properties.Add($"{property.Name}={propertyValueString}");
                }
                catch (Exception ex)
                {
                    properties.Add($"{property.Name}=<error:{ex.GetType().Name}>");
                }

                count++;
                if (count >= 12)
                {
                    break;
                }
            }

            if (properties.Count == 0)
            {
                return summary;
            }

            return $"{summary}, Props=[{string.Join(", ", properties)}]";
        }
    }

    /// <summary>
    /// Configuration for LocationInsideRegionValidator
    /// </summary>
    public class LocationInsideRegionConfig
    {
        /// <summary>
        /// Property path to extract region ID from dependency entity.
        /// Examples: "WgRegionId", "Town.WgRegionId"
        /// </summary>
        public string RegionPropertyPath { get; set; } = "WgRegionId";

        /// <summary>
        /// If true, allows location on region boundary.
        /// If false, strictly inside region.
        /// </summary>
        public bool AllowBoundary { get; set; } = false;
    }
}
