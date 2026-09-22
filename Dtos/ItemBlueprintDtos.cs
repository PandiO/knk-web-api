using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace knkwebapi_v2.Dtos
{
    // Read DTO - Full representation
    public class ItemBlueprintReadDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; } = string.Empty;

        [JsonPropertyName("iconMaterialRefId")]
        public int? IconMaterialRefId { get; set; }

        [JsonPropertyName("iconMaterialRef")]
        public MinecraftMaterialRefNavDto? IconMaterialRef { get; set; }

        [JsonPropertyName("defaultDisplayName")]
        public string DefaultDisplayName { get; set; } = string.Empty;

        [JsonPropertyName("defaultDisplayDescription")]
        public string? DefaultDisplayDescription { get; set; } = string.Empty;

        [JsonPropertyName("defaultQuantity")]
        public int DefaultQuantity { get; set; } = 1;

        [JsonPropertyName("maxStackSize")]
        public int MaxStackSize { get; set; } = 64;

        [JsonPropertyName("categoryId")]
        public int? CategoryId { get; set; }

        [JsonPropertyName("category")]
        public CategoryNavDto? Category { get; set; }

        [JsonPropertyName("gradeId")]
        public int? GradeId { get; set; }

        [JsonPropertyName("grade")]
        public GradeNavDto? Grade { get; set; }

        [JsonPropertyName("basePriceMin")]
        public decimal BasePriceMin { get; set; }

        [JsonPropertyName("basePriceMax")]
        public decimal BasePriceMax { get; set; }

        [JsonPropertyName("defaultEnchantments")]
        public List<ItemBlueprintDefaultEnchantmentDto> DefaultEnchantments { get; set; } = new();

        [JsonPropertyName("tags")]
        public List<ItemBlueprintTagDto> Tags { get; set; } = new();

        [JsonPropertyName("origins")]
        public List<ItemBlueprintOriginDto> Origins { get; set; } = new();
    }

    // Create DTO
    public class ItemBlueprintCreateDto
    {
        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("iconMaterialRefId")]
        public int? IconMaterialRefId { get; set; }

        // Optional: for hybrid picker - namespace key if material not persisted yet
        [JsonPropertyName("iconNamespaceKey")]
        public string? IconNamespaceKey { get; set; }

        [JsonPropertyName("defaultDisplayName")]
        public string DefaultDisplayName { get; set; } = string.Empty;

        [JsonPropertyName("defaultDisplayDescription")]
        public string DefaultDisplayDescription { get; set; } = string.Empty;

        [JsonPropertyName("defaultQuantity")]
        public int DefaultQuantity { get; set; } = 1;

        [JsonPropertyName("maxStackSize")]
        public int MaxStackSize { get; set; } = 64;

        [JsonPropertyName("categoryId")]
        public int? CategoryId { get; set; }

        [JsonPropertyName("gradeId")]
        public int? GradeId { get; set; }

        [JsonPropertyName("basePriceMin")]
        public decimal BasePriceMin { get; set; }

        [JsonPropertyName("basePriceMax")]
        public decimal BasePriceMax { get; set; }

        [JsonPropertyName("defaultEnchantments")]
        public List<ItemBlueprintDefaultEnchantmentCreateDto> DefaultEnchantments { get; set; } = new();

        [JsonPropertyName("tagIds")]
        public List<int> TagIds { get; set; } = new();

        [JsonPropertyName("origins")]
        public List<ItemBlueprintOriginCreateDto> Origins { get; set; } = new();
    }

    // Update DTO
    public class ItemBlueprintUpdateDto
    {
        [Required]
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("iconMaterialRefId")]
        public int? IconMaterialRefId { get; set; }

        [JsonPropertyName("iconNamespaceKey")]
        public string? IconNamespaceKey { get; set; }

        [JsonPropertyName("defaultDisplayName")]
        public string DefaultDisplayName { get; set; } = string.Empty;

        [JsonPropertyName("defaultDisplayDescription")]
        public string DefaultDisplayDescription { get; set; } = string.Empty;

        [JsonPropertyName("defaultQuantity")]
        public int DefaultQuantity { get; set; } = 1;

        [JsonPropertyName("maxStackSize")]
        public int MaxStackSize { get; set; } = 64;

        [JsonPropertyName("categoryId")]
        public int? CategoryId { get; set; }

        [JsonPropertyName("gradeId")]
        public int? GradeId { get; set; }

        [JsonPropertyName("basePriceMin")]
        public decimal BasePriceMin { get; set; }

        [JsonPropertyName("basePriceMax")]
        public decimal BasePriceMax { get; set; }

        [JsonPropertyName("defaultEnchantments")]
        public List<ItemBlueprintDefaultEnchantmentCreateDto> DefaultEnchantments { get; set; } = new();

        [JsonPropertyName("tagIds")]
        public List<int> TagIds { get; set; } = new();

        [JsonPropertyName("origins")]
        public List<ItemBlueprintOriginCreateDto> Origins { get; set; } = new();
    }

    // Navigation DTO - Lightweight representation for relationships
    public class ItemBlueprintNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("iconMaterialRefId")]
        public int? IconMaterialRefId { get; set; }

        [JsonPropertyName("defaultDisplayName")]
        public string DefaultDisplayName { get; set; } = string.Empty;
    }

    // List DTO - For search/list views
    public class ItemBlueprintListDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("defaultDisplayName")]
        public string DefaultDisplayName { get; set; } = string.Empty;

        [JsonPropertyName("iconMaterialRefId")]
        public int? IconMaterialRefId { get; set; }

        [JsonPropertyName("iconNamespaceKey")]
        public string? IconNamespaceKey { get; set; }

        [JsonPropertyName("categoryId")]
        public int? CategoryId { get; set; }

        [JsonPropertyName("categoryName")]
        public string? CategoryName { get; set; }

        [JsonPropertyName("gradeId")]
        public int? GradeId { get; set; }

        [JsonPropertyName("gradeName")]
        public string? GradeName { get; set; }

        [JsonPropertyName("basePriceMin")]
        public decimal BasePriceMin { get; set; }

        [JsonPropertyName("basePriceMax")]
        public decimal BasePriceMax { get; set; }

        [JsonPropertyName("defaultEnchantmentsCount")]
        public int DefaultEnchantmentsCount { get; set; }

        [JsonPropertyName("tagsCount")]
        public int TagsCount { get; set; }
    }

    // Navigation DTO for Category (e.g. ItemBlueprint.Category)
    public class CategoryNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    // Join Entity DTOs
    public class ItemBlueprintTagDto
    {
        [JsonPropertyName("itemBlueprintId")]
        public int ItemBlueprintId { get; set; }

        [JsonPropertyName("tagId")]
        public int TagId { get; set; }

        [JsonPropertyName("tag")]
        public TagNavDto? Tag { get; set; }
    }

    public class ItemBlueprintDefaultEnchantmentDto
    {
        [JsonPropertyName("itemBlueprintId")]
        public int ItemBlueprintId { get; set; }

        [JsonPropertyName("enchantmentDefinitionId")]
        public int EnchantmentDefinitionId { get; set; }

        [JsonPropertyName("enchantmentDefinition")]
        public EnchantmentDefinitionNavDto? EnchantmentDefinition { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; } = 1;
    }

    public class ItemBlueprintDefaultEnchantmentCreateDto
    {
        [Required]
        [JsonPropertyName("enchantmentDefinitionId")]
        public int EnchantmentDefinitionId { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; } = 1;
    }

    // Navigation DTO for MinecraftMaterialRef (reuse pattern from Category)
    public class MinecraftMaterialRefNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("namespaceKey")]
        public string NamespaceKey { get; set; } = string.Empty;

        [JsonPropertyName("legacyName")]
        public string? LegacyName { get; set; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }
    }

    // Navigation DTO for EnchantmentDefinition
    public class EnchantmentDefinitionNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("maxLevel")]
        public int MaxLevel { get; set; } = 1;

        [JsonPropertyName("isCustom")]
        public bool IsCustom { get; set; }
    }
}
