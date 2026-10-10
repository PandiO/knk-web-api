using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Properties;
using Microsoft.Extensions.DependencyInjection;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Provides metadata about form-configurable entities and their properties.
    /// 
    /// PURPOSE:
    /// This service enables form validation by inspecting model classes at runtime.
    /// It discovers all entities marked with [FormConfigurableEntity], extracts their properties,
    /// and caches detailed metadata including types, nullability, relationships, and default values.
    /// 
    /// DESIGN DECISIONS:
    /// 
    /// 1. CACHING STRATEGY
    ///    - Metadata is scanned once at startup and cached in memory
    ///    - Rationale: Entity structure is static; scanning on every request would waste CPU
    ///    - Cache is read-only after initialization, ensuring thread-safety
    ///    
    /// 2. INHERITANCE SUPPORT
    ///    - Includes properties from parent classes using BindingFlags.FlattenHierarchy
    ///    - Rationale: Forms for Town need to include fields from Domain parent class
    ///    - Without this, validation would incorrectly reject inherited properties
    ///    
    /// 3. DEFAULT VALUE DETECTION
    ///    - Instantiates each entity type to detect property initializers
    ///    - Rationale: Allows forms to mark fields with defaults as optional
    ///    - Example: AllowEntry = true in the model → can be optional in forms
    ///    
    /// 4. REFLECTION-BASED DISCOVERY
    ///    - Uses reflection to scan attributes and types dynamically
    ///    - Rationale: No need to manually maintain entity lists; automatic with [FormConfigurableEntity]
    ///    - New entities are automatically included without code changes
    /// </summary>
    public class MetadataService : IMetadataService
    {
        private readonly List<EntityMetadataDto> _cachedMetadata;
        private readonly NullabilityInfoContext _nullabilityInfoContext = new();
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// Initializes the service by scanning all form-configurable entities and caching their metadata.
        /// This happens once at application startup.
        /// </summary>
        public MetadataService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
            _cachedMetadata = ScanEntities();
        }

        /// <summary>
        /// Returns all cached entity metadata.
        /// 
        /// USAGE:
        /// - Used by metadata endpoints to expose entity information to clients
        /// - Allows UI/frontend to discover available entities and their properties
        /// - Example: /api/metadata endpoint returns this to populate entity dropdowns
        /// </summary>
        public List<EntityMetadataDto> GetAllEntityMetadata()
        {
            return ApplyDefaultTableColumns(_cachedMetadata);
        }

        /// <summary>
        /// Returns the names of all form-configurable entities.
        /// 
        /// USAGE:
        /// - UI needs to know which entities support forms
        /// - Endpoints like /api/form-configurations/{entityName} need a list of valid names
        /// - Example: Return ["Town", "Domain", "Location", "Street"] to populate entity selector
        /// </summary>
        public List<string> GetEntityNames()
        {
            return _cachedMetadata.Select(e => e.EntityName).ToList();
        }

        /// <summary>
        /// Retrieves metadata for a specific entity by name.
        /// 
        /// PARAMETERS:
        /// - entityName: Case-insensitive entity name (e.g., "Town", "town", "TOWN" all work)
        /// 
        /// RETURNS:
        /// - EntityMetadataDto containing all fields, types, relationships, and defaults
        /// - null if entity is not form-configurable
        /// 
        /// USAGE:
        /// - Called during form validation to check if a field exists and matches the schema
        /// - Called by endpoints to get full entity structure
        /// 
        /// EXAMPLE:
        /// var metadata = service.GetEntityMetadata("Town");
        /// // Returns all 10 fields: Name, Description, AllowEntry, AllowExit, WgRegionId, 
        /// // Location, CreatedAt, Streets, Districts, Id
        /// </summary>
        public EntityMetadataDto? GetEntityMetadata(string entityName)
        {
            return ApplyDefaultTableColumns(_cachedMetadata)
                .FirstOrDefault(e => e.EntityName.Equals(entityName, StringComparison.OrdinalIgnoreCase));
        }

        private List<EntityMetadataDto> ApplyDefaultTableColumns(List<EntityMetadataDto> source)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<KnKDbContext>();

            var configMap = dbContext.EntityTypeConfigurations
                .Where(c => !string.IsNullOrWhiteSpace(c.DefaultTableColumnsJson))
                .ToList()
                .ToDictionary(
                    c => c.EntityTypeName,
                    c => ParseColumns(c.DefaultTableColumnsJson),
                    StringComparer.OrdinalIgnoreCase
                );

            return source.Select(meta =>
            {
                var clone = new EntityMetadataDto
                {
                    EntityName = meta.EntityName,
                    DisplayName = meta.DisplayName,
                    Fields = meta.Fields,
                    DefaultTableColumns = configMap.TryGetValue(meta.EntityName, out var columns)
                        ? columns
                        : meta.DefaultTableColumns
                };

                return clone;
            }).ToList();
        }

        private static List<string>? ParseColumns(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json)
                    ?.Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Discovers all form-configurable entities and builds their metadata.
        /// 
        /// PROCESS:
        /// 1. Uses reflection to scan all types in the current assembly
        /// 2. Finds types decorated with [FormConfigurableEntity] attribute
        /// 3. For each entity, extracts field metadata (types, relationships, defaults)
        /// 4. Caches everything in memory for fast lookups
        /// 
        /// REFLECTION DETAILS:
        /// - Assembly.GetExecutingAssembly(): Gets the currently executing assembly (knkwebapi_v2)
        /// - GetTypes(): Returns all types defined in this assembly
        /// - GetCustomAttribute<T>(): Checks for [FormConfigurableEntity] marking
        /// 
        /// WHY REFLECTION?
        /// - Automatic discovery: No manual registration needed
        /// - DRY principle: Entity structure is already defined in model classes
        /// - Extensible: New entities work automatically with @FormConfigurableEntity decorator
        /// 
        /// WHEN IS THIS CALLED?
        /// - Once during application startup (in constructor)
        /// - Not called again unless service is recreated
        /// - Results are cached and reused for all requests
        /// 
        /// PERFORMANCE NOTE:
        /// - Reflection is expensive, but only runs once at startup
        /// - Entity instantiation (for default detection) happens once per field
        /// - Subsequent calls are O(1) cache lookups
        /// </summary>
        private List<EntityMetadataDto> ScanEntities()
        {
            var metadata = new List<EntityMetadataDto>();
            var assembly = Assembly.GetExecutingAssembly();

            // Step 1: Find all types that are marked as form-configurable
            var entityTypes = assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<FormConfigurableEntityAttribute>() != null);

            // Step 2: Extract metadata for each entity
            foreach (var entityType in entityTypes)
            {
                var attribute = entityType.GetCustomAttribute<FormConfigurableEntityAttribute>();
                if (attribute == null) continue;

                // Create metadata object with entity name, display name, and all fields
                var entityMetadata = new EntityMetadataDto
                {
                    EntityName = entityType.Name,  // e.g., "Town"
                    DisplayName = attribute.DisplayName,  // e.g., "Towns"
                    Fields = GetFieldMetadata(entityType)  // All properties with full details
                };

                metadata.Add(entityMetadata);
            }

            return metadata;
        }

        /// <summary>
        /// Retrieves field metadata for an entity type, including inherited properties from parent classes.
        /// This ensures that child entities (like Town inheriting from Domain) include all parent fields.
        /// Also detects default values assigned to properties in the model class.
        /// 
        /// KEY DESIGN DECISION: INHERITANCE SUPPORT
        /// Problem: Without FlattenHierarchy, inherited fields are lost
        /// Example: Town extends Domain
        ///   - Without FlattenHierarchy: Only Streets, Districts are found
        ///   - With FlattenHierarchy: Also includes Name, Description, AllowEntry, AllowExit, etc.
        /// Impact: Validation would incorrectly reject Town forms using Domain's fields
        /// 
        /// BindingFlags Explanation:
        /// - Public: Only public properties (ignore private/protected)
        /// - Instance: Only instance properties (ignore static)
        /// - FlattenHierarchy: IMPORTANT! Include properties from base classes
        ///   (Only works with GetProperties on the derived type)
        /// 
        /// PROCESS FOR EACH PROPERTY:
        /// 1. Check for [RelatedEntityField] attribute (indicates foreign key/navigation)
        /// 2. Extract type information and nullable status
        /// 3. Detect if property has a default value (e.g., = true, = new Collection())
        /// 4. Build FieldMetadataDto with all details
        /// 
        /// DEFAULT VALUE DETECTION IMPORTANCE:
        /// - Non-nullable fields CAN be optional in forms if they have defaults
        /// - Example: bool AllowEntry = true; can be optional in forms
        /// - Without this, validation would incorrectly require all non-nullable fields
        /// 
        /// EXAMPLE OUTPUT FOR Town:
        /// Field: Name (from Domain)
        ///   - FieldType: String
        ///   - IsNullable: true (string reference type)
        ///   - HasDefaultValue: false
        /// 
        /// Field: AllowEntry (from Domain)
        ///   - FieldType: Boolean
        ///   - IsNullable: false (value type)
        ///   - HasDefaultValue: true (= true in model)
        ///   - DefaultValue: "True"
        /// 
        /// Field: Location (from Domain)
        ///   - FieldType: Location
        ///   - IsRelatedEntity: true ([RelatedEntityField] attribute)
        ///   - RelatedEntityType: Location
        /// 
        /// Field: Streets (from Town)
        ///   - FieldType: Collection
        ///   - IsRelatedEntity: true
        ///   - RelatedEntityType: Street
        ///   - HasDefaultValue: true (= new Collection() in model)
        /// </summary>
        /// <summary>
        /// Converts CLR type names to friendly type names expected by the frontend.
        /// 
        /// PURPOSE:
        /// The frontend uses friendly type names (String, Integer, Boolean, Decimal, etc.)
        /// but .NET reflection returns CLR type names (String, Int32, Boolean, Single, Double, etc.)
        /// This mapping ensures consistency and allows the frontend to recognize all types.
        /// 
        /// EXAMPLES:
        /// - Single → Float (System.Single is the CLR name for C# float)
        /// - Int32 → Integer (System.Int32 is the CLR name for C# int)
        /// - Int64 → Integer (System.Int64 maps to integer family)
        /// - Double → Decimal (floating point types → Decimal for form purposes)
        /// - String → String (passes through)
        /// - Boolean → Boolean (passes through)
        /// - Any other type → the original type name (will fallback to Object in frontend)
        /// 
        /// RATIONALE:
        /// Numeric floating point types (Single, Double) should be treated as Decimal
        /// in forms to maintain precision consistency. This prevents unexpected rounding
        /// errors when users enter decimal values.
        /// </summary>
        private string GetFriendlyTypeName(Type type)
        {
            var typeName = type.Name;
            
            // Map CLR type names to friendly names
            var typeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Floating point types
                { "Single", "Float" },      // C# float → System.Single
                { "Double", "Decimal" },   // C# double → System.Double
                
                // Integer types
                { "Int32", "Integer" },    // C# int → System.Int32
                { "Int64", "Integer" },    // C# long → System.Int64
                { "Int16", "Integer" },    // C# short → System.Int16
                
                // Keep these as-is (already friendly)
                { "String", "String" },
                { "Boolean", "Boolean" },
                { "DateTime", "DateTime" },
                { "Decimal", "Decimal" },
                
                // Collections
                { "List`1", "List" },
                { "Collection`1", "List" },
                { "ICollection`1", "List" }
            };
            
            // Try to find a mapping
            if (typeMapping.TryGetValue(typeName, out var friendlyName))
            {
                return friendlyName;
            }
            
            // If type ends with `1 or `2, it's a generic collection - map to List
            if (typeName.Contains("`"))
            {
                return "List";
            }
            
            // Return the original name as fallback
            return typeName;
        }

        private List<FieldMetadataDto> GetFieldMetadata(Type entityType)
        {
            var fields = new List<FieldMetadataDto>();
            // Use FlattenHierarchy to include properties from parent classes
            // CRITICAL: This ensures inherited properties (like Town from Domain) are included
            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            foreach (var property in properties)
            {
                var relatedEntityAttr = property.GetCustomAttribute<RelatedEntityFieldAttribute>();
                var fieldType = property.PropertyType;
                var underlyingType = Nullable.GetUnderlyingType(fieldType);
                var isNullable = IsPropertyNullable(property, fieldType, underlyingType);

                // Check for default value via property initializer
                // This allows fields with defaults to be optional in forms
                var (hasDefaultValue, defaultValueStr) = ExtractDefaultValue(property, entityType);

                var fieldMetadata = new FieldMetadataDto
                {
                    FieldName = property.Name,
                    FieldType = GetFriendlyTypeName(underlyingType ?? fieldType),
                    IsNullable = isNullable,
                    IsRelatedEntity = relatedEntityAttr != null,
                    RelatedEntityType = relatedEntityAttr?.RelatedEntityType.Name,
                    HasDefaultValue = hasDefaultValue,
                    DefaultValue = defaultValueStr
                };

                var enumType = underlyingType ?? fieldType;
                if (enumType.IsEnum)
                {
                    // GetFriendlyTypeName has no enum branch, so it would otherwise return the raw
                    // CLR enum name (e.g. "GateInfoDisplayMode"), which the frontend's mapFieldType
                    // doesn't recognize and FormTemplateValidationService doesn't treat as an enum.
                    fieldMetadata.FieldType = "Enum";
                    fieldMetadata.IsEnum = true;
                    fieldMetadata.EnumTypeName = enumType.Name;
                    fieldMetadata.EnumValues = Enum.GetNames(enumType).ToList();
                }

                fields.Add(fieldMetadata);
            }

            return fields;
        }

        private bool IsPropertyNullable(PropertyInfo property, Type fieldType, Type? underlyingType)
        {
            if (fieldType.IsValueType)
            {
                return underlyingType != null;
            }

            if (property.GetCustomAttribute<RequiredMemberAttribute>() != null)
            {
                return false;
            }

            var nullability = _nullabilityInfoContext.Create(property);

            if (nullability.ReadState == NullabilityState.NotNull)
            {
                return false;
            }

            if (nullability.ReadState == NullabilityState.Nullable)
            {
                return true;
            }

            return true;
        }

        /// <summary>
        /// Extracts default value information from a property initializer.
        /// Analyzes instances to detect property assignments.
        /// This handles cases like: public string Name { get; set; } = "default value";
        /// 
        /// WHY THIS METHOD?
        /// Problem: C# doesn't provide reflection API to read initializer expressions directly
        /// Solution: Create an instance and check what values properties have
        /// 
        /// APPROACH:
        /// 1. Instantiate the entity type using its parameterless constructor
        /// 2. Get the property value from that instance
        /// 3. Determine if it's a meaningful default (non-null, non-empty)
        /// 4. Return formatted default value string
        /// 
        /// EXAMPLE:
        /// Model: public bool AllowEntry { get; set; } = true;
        /// Process:
        ///   1. Create instance of Town
        ///   2. Get AllowEntry value → true
        ///   3. Return (HasDefault: true, DefaultValue: "True")
        /// 
        /// HANDLES:
        /// - Value types: bool, int, DateTime, decimal → default displayed as string
        /// - Strings: Only if non-empty (empty strings = no meaningful default)
        /// - Collections: new Collection<>() → detected and reported
        /// - References: Most reference types are ignored as they don't have meaningful defaults
        /// 
        /// GRACEFUL FAILURE:
        /// - If instantiation fails, returns (false, null)
        /// - Does not throw; validation continues with assumption of "no default"
        /// - Example: If constructor throws, field is treated as having no default
        /// </summary>
        private (bool HasDefault, string? DefaultValue) ExtractDefaultValue(PropertyInfo property, Type declaringType)
        {
            var explicitDefault = property.GetCustomAttribute<DefaultValueAttribute>();
            if (explicitDefault != null)
            {
                return (true, explicitDefault.Value?.ToString());
            }

            try
            {
                // Try to instantiate the type and check if the property has a value
                return DetectDefaultValueFromConstructor(property, declaringType);
            }
            catch
            {
                // If reflection fails, assume no default
                // This is safe: missing default detection is better than crashing
                return (false, null);
            }
        }

        /// <summary>
        /// Detects if a property has a default value by analyzing the type's constructors.
        /// Looks at field initializers used in C# syntax like: public string Name = "value";
        /// 
        /// TECHNICAL DETAILS:
        /// 
        /// HOW C# INITIALIZERS WORK:
        /// In C#, when you write:
        ///   public class Town {
        ///     public string Name = "default";
        ///     public bool AllowEntry = true;
        ///   }
        /// 
        /// The compiler generates code in the constructor that assigns these values:
        ///   public Town() {
        ///     this.Name = "default";
        ///     this.AllowEntry = true;
        ///   }
        /// 
        /// So by creating an instance and reading properties, we see the initialized values!
        /// 
        /// DETECTION LOGIC:
        /// 
        /// Value Types (int, bool, DateTime, decimal):
        ///   - These always have a default in C# (.NET)
        ///   - Default for bool: false
        ///   - Default for int: 0
        ///   - Default for DateTime: DateTime.MinValue
        ///   - If you initialize: public bool AllowEntry = true;
        ///   - We detect and report: HasDefault: true, DefaultValue: "True"
        /// 
        /// String Types:
        ///   - Most strings have null as default (no initialization)
        ///   - Only report as "has default" if explicitly initialized to non-empty value
        ///   - Example: public string City = "DefaultCity"; → reports as default
        ///   - Example: public string Description; → no default (remains null)
        /// 
        /// Reference Types (Collections, Objects):
        ///   - Typically don't have defaults (null by default)
        ///   - EXCEPT collections initialized with new Collection<>()
        ///   - Example: public ICollection<Street> Streets = new Collection<Street>();
        ///   - We detect this and report: HasDefault: true, DefaultValue: "new Collection()"
        /// 
        /// WHY DETECT COLLECTION DEFAULTS?
        /// - Forms can mark collection fields as optional if they have defaults
        /// - The form won't require users to enter Streets/Districts
        /// - The database will use the default empty collection
        /// - This improves UX: users only fill required fields
        /// 
        /// FILTERING:
        /// - Ignores internal .NET types (System.Collections, System.Diagnostics, etc.)
        /// - Ignores model namespace types (knkwebapi_v2.Models) as they're not meaningful defaults
        /// - Focus on user-defined defaults that matter for forms
        /// </summary>
        private (bool HasDefault, string? DefaultValue) DetectDefaultValueFromConstructor(PropertyInfo property, Type declaringType)
        {
            try
            {
                // Get the default constructor (parameterless)
                // Properties with values must be initialized in the parameterless constructor
                var ctors = declaringType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                var defaultCtor = ctors.FirstOrDefault(c => c.GetParameters().Length == 0);

                if (defaultCtor != null)
                {
                    // Create an instance and check if the property has a value
                    // By creating an instance, C# initializers automatically run
                    try
                    {
                        var instance = Activator.CreateInstance(declaringType);
                        if (instance == null) return (false, null);

                        var value = property.GetValue(instance);

                        if (value != null)
                        {
                            // Determine if this is truly a default value
                            if (property.PropertyType.IsValueType)
                            {
                                // Value types always have a default (e.g., bool: false, int: 0)
                                // If we got here with a non-null value, it's been initialized
                                return (true, value.ToString());
                            }
                            else if (property.PropertyType == typeof(string))
                            {
                                // For strings, non-empty values are defaults
                                // Empty strings or null don't count as meaningful defaults
                                if (!string.IsNullOrEmpty(value.ToString()))
                                {
                                    return (true, value.ToString());
                                }
                            }
                            else
                            {
                                // For reference types (e.g., new Collection<>())
                                var valueStr = value.ToString();
                                
                                // Skip internal/auto-generated types that aren't user-defined defaults
                                if (valueStr != null && !valueStr.StartsWith("knkwebapi_v2.Models") && !valueStr.StartsWith("System.Collections"))
                                {
                                    return (true, valueStr);
                                }
                                else if (valueStr?.StartsWith("System.Collections") == true)
                                {
                                    // Collections are special: even though they're System.Collections,
                                    // they represent user-initialized defaults (new Collection<>())
                                    // This is important for forms: allows collection fields to be optional
                                    return (true, "new Collection()");
                                }
                            }
                        }
                        else if (Nullable.GetUnderlyingType(property.PropertyType) != null)
                        {
                            // A nullable value type (int?, bool?, an enum?) without an initializer defaults to null,
                            // which is no default. Activator.CreateInstance(typeof(T?)) also returns null, so the
                            // branch below would report the placeholder "default" - copied into a form field's
                            // DefaultValue and then submitted in place of an empty value (KNG-119).
                            return (false, null);
                        }
                        else if (property.PropertyType.IsValueType && property.PropertyType != typeof(void))
                        {
                            // Value types have implicit defaults (bool: false, int: 0, etc.)
                            // Even if not explicitly initialized, they have a default
                            var defaultValue = Activator.CreateInstance(property.PropertyType);
                            return (true, defaultValue?.ToString() ?? "default");
                        }
                    }
                    catch
                    {
                        // If instantiation fails, we can't detect the default
                        // Return (false, null) to be safe
                    }
                }

                return (false, null);
            }
            catch
            {
                return (false, null);
            }
        }
    }
}
