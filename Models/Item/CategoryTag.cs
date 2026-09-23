using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("CategoryTag")]
public class CategoryTag
{
    // Composite primary key (Category + Tag combo)
    [NavigationPair(nameof(Category))]
    [RelatedEntityField(typeof(Category))]
    public int CategoryId { get; set; }
    [RelatedEntityField(typeof(Category))]
    public Category Category { get; set; } = null!;

    [NavigationPair(nameof(Tag))]
    [RelatedEntityField(typeof(Tag))]
    public int TagId { get; set; }
    [RelatedEntityField(typeof(Tag))]
    public Tag Tag { get; set; } = null!;
}
