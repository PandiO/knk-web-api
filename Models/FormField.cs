using System;
using System.Collections.Generic;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models
{
    /// <summary>
    /// Represents a single input field in a form step.
    /// Maps to a property on the target entity (e.g., Shop.Name, Shop.LocationId).
    /// 
    /// COPY-ON-REUSE PATTERN (same as FormStep):
    /// - Fields can be marked as reusable templates (IsReusable = true, FormStepId = null).
    /// - When adding a reusable field to a step, it is CLONED.
    /// - SourceFieldId tracks the template that was copied.
    /// 
    /// FIELD TYPES:
    /// - Simple types: String, Integer, Boolean, DateTime, Decimal
    /// - Complex types: Object (references another entity), List (collection of items), Enum
    /// 
    /// DEPENDENCIES AND SUB-FORMS:
    /// - Fields can depend on other fields (conditional rendering).
    /// - Object-type fields can launch nested form configurations.
    /// </summary>
    public class FormField
    {
        public int Id { get; set; }
        
        /// <summary>
        /// Unique identifier for this field instance. Used in FieldOrderJson to reference this specific field.
        /// </summary>
        public Guid FieldGuid { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// The actual property name on the target entity.
        /// Example: For a Shop entity with property "Name", this would be "Name".
        /// Used by backend to map form input to entity properties (via AutoMapper or manual mapping).
        /// </summary>
        public string FieldName { get; set; } = null!;
        
        /// <summary>
        /// Display label shown in the UI (can be different from FieldName for better UX).
        /// Example: FieldName = "LocationId", Label = "Select Location".
        /// </summary>
        public string Label { get; set; } = null!;
        
        /// <summary>
        /// Placeholder text shown in empty input fields (e.g., "Enter shop name...").
        /// </summary>
        public string? Placeholder { get; set; }
        
        /// <summary>
        /// Optional help text or tooltip shown to users (e.g., "Choose the primary category for this item").
        /// </summary>
        public string? Description { get; set; }
        
        /// <summary>
        /// The data type of this field. Determines which input control to render in the UI.
        /// - String → text input
        /// - Integer/Decimal → number input
        /// - Boolean → checkbox/toggle
        /// - DateTime → date picker
        /// - Enum → dropdown/select
        /// - Object → autocomplete/search (references another entity)
        /// - List → multi-select or repeating sub-form
        /// </summary>
        public FieldType FieldType { get; set; }

        /// <summary>
        /// For FieldType = List, specifies the type of elements in the collection.
        /// </summary>
        public FieldType? ElementType { get; set; }
        
        /// <summary>
        /// Required when FieldType or ElementType = Object.
        /// Specifies the entity type this field references (e.g., "Domain", "Category", "User").
        /// 
        /// USE CASE: When rendering the field, the frontend knows to fetch options from /api/domains or /api/categories.
        /// When creating a new object, it can launch the nested form configuration for that entity type.
        /// </summary>
        public string? ObjectType { get; set; }
        
        /// <summary>
        /// Required when FieldType = Enum.
        /// Specifies the enum type name (e.g., "ItemRarity", "ShopStatus").
        /// Frontend fetches enum values from a metadata endpoint or uses a hardcoded mapping.
        /// </summary>
        public string? EnumType { get; set; }
        
        /// <summary>
        /// Optional default value to pre-fill when the form loads.
        /// Stored as string; frontend converts to appropriate type based on FieldType.
        /// </summary>
        public string? DefaultValue { get; set; }
        
        /// <summary>
        /// If true, this field must be filled before the user can proceed to the next step.
        /// Validated on both frontend (UX) and backend (security).
        /// </summary>
        public bool Required { get; set; } = true;
        
        /// <summary>
        /// If true, the field is displayed but cannot be edited (e.g., auto-generated IDs, calculated values).
        /// </summary>
        public bool ReadOnly { get; set; } = false;
        
        /// <summary>
        /// If true, this field is stored in the reusable field library (FormStepId = null).
        /// When added to a step, it is cloned (copy-on-reuse pattern).
        /// </summary>
        public bool IsReusable { get; set; } = false;
        
        /// <summary>
        /// Tracks which reusable field template was used to create this field instance.
        /// NULL if this field was created from scratch (not cloned).
        /// 
        /// BEHAVIOR DEPENDS ON IsLinkedToSource:
        /// - If IsLinkedToSource = false (Copy mode): This is a full clone. SourceFieldId is for traceability only.
        /// - If IsLinkedToSource = true (Link mode): This is a reference to the source. Display properties come from source.
        /// </summary>
        public int? SourceFieldId { get; set; }
        
        /// <summary>
        /// Indicates whether this field is linked to a source template (true) or is an independent copy (false).
        /// 
        /// Link mode (IsLinkedToSource = true):
        /// - This field references the source template (SourceFieldId).
        /// - Display properties (FieldName, Label, FieldType, etc.) are loaded from source at read-time.
        /// - Changes to the source template are immediately visible in linked instances.
        /// - The field's position can still be customized per step via FieldOrderJson.
        /// 
        /// Copy mode (IsLinkedToSource = false):
        /// - This field is a full clone of the source, fully independent after creation.
        /// - All properties (FieldName, Label, FieldType, validations, etc.) are owned by this instance.
        /// - Changes to the source template do NOT affect this copy.
        /// - SourceFieldId is kept only for traceability/analytics purposes.
        /// 
        /// Default: false (copy mode is the standard behavior).
        /// </summary>
        public bool IsLinkedToSource { get; set; } = false;
        
        /// <summary>
        /// Creates a dependency relationship: this field is only shown/enabled when another field meets a condition.
        /// References the Id of another FormField (can be in the same step OR a previous step).
        /// 
        /// EXAMPLE 1 (same step):
        /// - Field A: "Do you want advanced settings?" (Boolean)
        /// - Field B: "Max Capacity" depends on Field A being true.
        /// - If user unchecks Field A, Field B is hidden.
        /// 
        /// EXAMPLE 2 (previous step):
        /// - Step 1, Field: "Shop Type" (Enum: Physical, Digital, Hybrid)
        /// - Step 2, Field: "Store Address" depends on "Shop Type" = Physical or Hybrid.
        /// - If user selected Digital in Step 1, "Store Address" is hidden in Step 2.
        /// 
        /// The actual condition logic is stored in DependencyConditionJson.
        /// </summary>
        public int? DependsOnFieldId { get; set; }
        
        /// <summary>
        /// Navigation property for the field this field depends on.
        /// Allows EF Core to load the dependency when fetching configurations.
        /// </summary>
        public FormField? DependsOnField { get; set; }
        
        /// <summary>
        /// JSON object storing the dependency logic.
        /// 
        /// WHY JSON instead of a dedicated ConditionType enum and parameters table?
        /// 1. FLEXIBILITY: Supports complex conditions without schema changes.
        ///    Simple: { "operator": "equals", "value": true }
        ///    Complex: { "operator": "in", "values": ["Physical", "Hybrid"] }
        ///    Advanced: { "operator": "and", "conditions": [...] } (nested logic)
        /// 2. EXTENSIBILITY: New condition types can be added without database migrations.
        /// 3. FRONTEND CONTROL: React can interpret and render conditions dynamically.
        /// 
        /// Example JSON formats:
        /// Simple equality: { "operator": "equals", "value": "Physical" }
        /// Range check: { "operator": "greaterThan", "value": 10 }
        /// Multiple values: { "operator": "in", "values": ["Option1", "Option2"] }
        /// Combined logic: { "operator": "and", "conditions": [{ "operator": "equals", "value": true }, ...] }
        /// 
        /// The frontend evaluates this condition and shows/hides the field accordingly.
        /// Backend can also validate that required dependent fields are filled based on this logic.
        /// </summary>
        public string? DependencyConditionJson { get; set; }
        
        /// <summary>
        /// References a FormConfiguration for creating the referenced object inline.
        /// Only relevant when FieldType = Object.
        /// 
        /// EXAMPLE SCENARIO:
        /// - Field: "Domain" (ObjectType = "Domain")
        /// - User is filling out a Shop creation form.
        /// - The form has a "Domain" field (autocomplete dropdown).
        /// - User clicks "+ Create New Domain" button.
        /// - Frontend launches the Domain creation wizard (fetches FormConfiguration for "Domain").
        /// - User completes the Domain form (nested wizard).
        /// - Upon completion, control returns to the Shop form, and the newly created Domain is selected.
        /// 
        /// SubConfigurationId specifies WHICH configuration to use for that object type.
        /// NULL = use the default configuration for that ObjectType.
        /// NON-NULL = use a specific configuration (e.g., "Quick Domain Creation" vs. "Advanced Domain Setup").
        /// 
        /// This enables recursive form wizards: Shop → Domain → Location → etc.
        /// FormSubmissionProgress tracks nested progress so users can pause/resume at any level.
        /// </summary>
        public int? SubConfigurationId { get; set; }

        /// <summary>
        /// List relationship fields: fewest / most items the wizard accepts. NULL = no limit.
        /// KNG-164: these and CanCreate/IncrementValue were on FormFieldDto only and dropped on save.
        /// </summary>
        public int? MinSelection { get; set; }
        public int? MaxSelection { get; set; }

        /// <summary>
        /// Relationship fields: whether "Create New" is offered. NULL = yes (the web app treats only
        /// false as "no").
        /// </summary>
        public bool? CanCreate { get; set; }

        /// <summary>Integer fields: step of the number input. NULL = 1.</summary>
        public int? IncrementValue { get; set; }
        
        /// <summary>
        /// Navigation property for the nested configuration.
        /// Loaded when the frontend needs to render the sub-form wizard.
        /// </summary>
        public FormConfiguration? SubConfiguration { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Foreign key to the step this field belongs to.
        /// NULL for reusable template fields (stored in library).
        /// NON-NULL for fields that are part of a specific step instance.
        /// </summary>
        public int? FormStepId { get; set; }
        public FormStep? FormStep { get; set; }
        
        /// <summary>
        /// Validation rules applied to this field (e.g., min/max length, required, regex pattern).
        /// Each validation has a type and parameters stored as JSON.
        /// </summary>
        public List<FieldValidation> Validations { get; set; } = new();
        
        /// <summary>
        /// Field-level validation rules (NEW).
        /// 
        /// These rules are executed when the field value changes or before form submission.
        /// Each rule can depend on another field's value for cross-field validation.
        /// 
        /// EXAMPLE:
        /// - Field: "SpawnLocationId" (Location)
        /// - Rule: "LocationInsideRegion" (depends on "WgRegionId")
        /// - Validation: Location coordinates must be inside the WorldGuard region
        /// - Result: ✅ success or ❌ failure with error message (blocking or warning)
        /// </summary>
        public List<FieldValidationRule> ValidationRules { get; set; } = new();
        
        /// <summary>
        /// Collection of fields that depend on THIS field.
        /// Inverse navigation property for DependsOnField.
        /// 
        /// USE CASE: When this field's value changes, the frontend can quickly find all dependent fields
        /// and re-evaluate their visibility/enabled state based on DependencyConditionJson.
        /// 
        /// EXAMPLE:
        /// - Field A: "Enable Advanced Mode" (Boolean)
        /// - Field B: "Cache Size" depends on Field A.
        /// - Field C: "Timeout" depends on Field A.
        /// - DependentFields for Field A = [Field B, Field C].
        /// 
        /// When user toggles Field A, frontend iterates DependentFields and shows/hides them accordingly.
        /// 
        /// Backend uses this for validation: if Field A is true, ensure Field B and Field C are filled.
        /// </summary>
        public List<FormField> DependentFields { get; set; } = new();
        
        /// <summary>
        /// JSON string for additional field settings that don't fit into dedicated properties.
        /// Used for field-specific configurations like:
        /// - HybridMinecraftMaterialRefPicker: { "categoryFilter": "ICON", "multiSelect": false }
        /// - World task integration: { "worldTask": { "enabled": true, "taskType": "VerifyLocation" } }
        /// - Custom field behaviors: { "allowCustom": true, "maxItems": 5 }
        /// 
        /// This provides extensibility without requiring schema changes for every new field configuration option.
        /// </summary>
        public string? SettingsJson { get; set; }

        /// <summary>
        /// Visibility rules for this field. Empty means the field is always shown.
        /// </summary>
        public List<DisplayConditionGroup> DisplayConditionGroups { get; set; } = new();

        /// <summary>
        /// Display conditions elsewhere in the configuration that read this field's value.
        /// </summary>
        public List<DisplayCondition> UsedInDisplayConditions { get; set; } = new();
    }
}
