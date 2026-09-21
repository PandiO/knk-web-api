using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // Used for both read and write; Id is ignored on create and required on update
    // (matches the ItemBlueprint Read/Create/Update split's intent, collapsed into
    // one shape per level since every level here is always authored as part of the
    // owning MenuTemplate's tree, not edited independently).
    public class MenuTemplateDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [Required]
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; } = 3;

        [JsonPropertyName("growth")]
        public string Growth { get; set; } = "Static";

        [JsonPropertyName("backgroundMaterialRefId")]
        public int? BackgroundMaterialRefId { get; set; }

        [JsonPropertyName("sections")]
        public List<MenuSectionTemplateDto> Sections { get; set; } = new();
    }

    public class MenuTemplateListDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("sectionCount")]
        public int SectionCount { get; set; }
    }

    public class MenuSectionTemplateDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "ContentGrid";

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }

        [JsonPropertyName("displaySlot")]
        public int DisplaySlot { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; } = 9;

        [JsonPropertyName("height")]
        public int Height { get; set; } = 1;

        [JsonPropertyName("positionMode")]
        public string PositionMode { get; set; } = "Static";

        [JsonPropertyName("alignVertical")]
        public string AlignVertical { get; set; } = "Top";

        [JsonPropertyName("alignHorizontal")]
        public string AlignHorizontal { get; set; } = "Left";

        [JsonPropertyName("overflow")]
        public string Overflow { get; set; } = "Hide";

        [JsonPropertyName("listMode")]
        public string ListMode { get; set; } = "Default";

        [JsonPropertyName("priority")]
        public string Priority { get; set; } = "Medium";

        [JsonPropertyName("visibilityPermission")]
        public string? VisibilityPermission { get; set; }

        [JsonPropertyName("items")]
        public List<MenuItemTemplateDto> Items { get; set; } = new();

        [JsonPropertyName("variableBindings")]
        public List<VariableBindingDto> VariableBindings { get; set; } = new();
    }

    public class MenuItemTemplateDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }

        [JsonPropertyName("slotOverride")]
        public int? SlotOverride { get; set; }

        [JsonPropertyName("materialRefId")]
        public int? MaterialRefId { get; set; }

        [JsonPropertyName("amount")]
        public int Amount { get; set; } = 1;

        [JsonPropertyName("chatColorName")]
        public string? ChatColorName { get; set; }

        [JsonPropertyName("chatColorDescription")]
        public string? ChatColorDescription { get; set; }

        [JsonPropertyName("displayMode")]
        public string DisplayMode { get; set; } = "Normal";

        [JsonPropertyName("visibilityPermission")]
        public string? VisibilityPermission { get; set; }

        [JsonPropertyName("actionPermission")]
        public string? ActionPermission { get; set; }

        [JsonPropertyName("variableBindings")]
        public List<VariableBindingDto> VariableBindings { get; set; } = new();

        [JsonPropertyName("actions")]
        public List<ActionBindingDto> Actions { get; set; } = new();

        [JsonPropertyName("conditions")]
        public List<ConditionBindingDto> Conditions { get; set; } = new();
    }

    public class VariableBindingDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [Required]
        [JsonPropertyName("targetProperty")]
        public string TargetProperty { get; set; } = string.Empty;

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }

        [Required]
        [JsonPropertyName("expression")]
        public string Expression { get; set; } = string.Empty;

        [JsonPropertyName("refreshPolicy")]
        public string RefreshPolicy { get; set; } = "OnDirty";

        [JsonPropertyName("ttlTicks")]
        public int? TtlTicks { get; set; }
    }

    public class ActionBindingDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [Required]
        [JsonPropertyName("actionTypeId")]
        public string ActionTypeId { get; set; } = string.Empty;

        [JsonPropertyName("paramsJson")]
        public string ParamsJson { get; set; } = "{}";

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }

        [JsonPropertyName("conditions")]
        public List<ConditionBindingDto> Conditions { get; set; } = new();
    }

    public class ConditionBindingDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [Required]
        [JsonPropertyName("conditionTypeId")]
        public string ConditionTypeId { get; set; } = string.Empty;

        [JsonPropertyName("paramsJson")]
        public string ParamsJson { get; set; } = "{}";

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }
    }
}
