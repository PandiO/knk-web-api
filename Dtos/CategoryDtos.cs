using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    public class CategoryDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("iconMaterialRefId")]
        public int? IconMaterialRefId { get; set; }
        // Optional namespace key for hybrid material picker selections when the material is not yet persisted
        [JsonPropertyName("iconNamespaceKey")]
        public string? IconNamespaceKey { get; set; }

        [JsonPropertyName("parentCategoryId")]
        public int? ParentCategoryId { get; set; }
        [JsonPropertyName("parentCategory")]
        public CategoryDto? ParentCategory { get; set; }

        // Children of this category
        [JsonPropertyName("childCategories")]
        public List<RelatedCategoryDto> ChildCategories { get; set; } = new();

        // Optional embedded icon material reference when available
        [JsonPropertyName("iconMaterialRef")]
        public MinecraftMaterialRefDto? IconMaterialRef { get; set; }

        // Read: populated with the related Tag for display. Write (Phase 2,
        // docs/specs/items/IMPLEMENTATION_PLAN.md §6/§4.2): the FormWizard M2M step engine submits this
        // field as an array of join entries keyed by TagId (CategoryTag carries no extra columns beyond
        // its two FKs) - CategoryId/Tag are ignored on write and reconciled by CategoryService.
        [JsonPropertyName("tags")]
        public List<CategoryTagDto> Tags { get; set; } = new();
    }

    public class CategoryTagDto
    {
        [JsonPropertyName("categoryId")]
        public int CategoryId { get; set; }

        [JsonPropertyName("tagId")]
        public int TagId { get; set; }

        [JsonPropertyName("tag")]
        public TagNavDto? Tag { get; set; }
    }

    public class RelatedCategoryDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; } = null!;

        [JsonPropertyName("iconMaterialRefId")]
        public int? IconMaterialRefId { get; set; }

        [JsonPropertyName("iconMaterialRef")]
        public MinecraftMaterialRefDto? IconMaterialRef { get; set; }
    }
    
    public class CategoryListDto
    {
        [JsonPropertyName("id")]
        public int? id { get; set; }
        [JsonPropertyName("name")]
        public string name { get; set; } = null!;
        [JsonPropertyName("parentCategoryName")]
        public string? parentCategoryName { get; set; }
        [JsonPropertyName("parentCategoryId")]
        public int? parentCategoryId { get; set; }

        [JsonPropertyName("iconMaterialRefId")]
        public int? iconMaterialRefId { get; set; }
        [JsonPropertyName("iconMaterialRefName")]
        public string? iconMaterialRefName { get; set; }

        [JsonPropertyName("iconNamespaceKey")]
        public string? iconNamespaceKey { get; set; }

        // Convenience: number of direct children
        [JsonPropertyName("childrenCount")]
        public int childrenCount { get; set; }
    }
}