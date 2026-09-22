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

        // Read-only for now (Phase 1, docs/specs/items/IMPLEMENTATION_PLAN.md §6/§4.2): the CategoryTag
        // join entity exists and is populated here for visibility, but write-side (create/update) support
        // is deferred to Phase 2 alongside Category's own Tags FormConfiguration step.
        [JsonPropertyName("tags")]
        public List<TagNavDto> Tags { get; set; } = new();
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