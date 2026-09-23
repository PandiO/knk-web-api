using System;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("Category")]
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public int? IconMaterialRefId { get; set; }
    
    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    public MinecraftMaterialRef? IconMaterialRef { get; set; }

    [NavigationPair(nameof(ParentCategory))]
    [RelatedEntityField(typeof(Category))]
    public int? ParentCategoryId { get; set; }

    [RelatedEntityField(typeof(Category))]
    public Category? ParentCategory { get; set; }

    // One-to-many: ParentCategoryId -> ChildCategories
    [RelatedEntityField(typeof(Category))]
    public ICollection<Category> ChildCategories { get; set; } = new List<Category>();

    [RelatedEntityField(typeof(CategoryTag))]
    public ICollection<CategoryTag> Tags { get; set; } = new List<CategoryTag>();
}
