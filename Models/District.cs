using System;
using System.Collections.ObjectModel;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("District")]
public class District : Domain
{
    [NavigationPair(nameof(Town))]
    [RelatedEntityField(typeof(Town))]
    public int TownId { get; set; }
    [RelatedEntityField(typeof(Town))]
    public Town Town { get; set; } = null!;
    /// <summary>A capital district's territory range is multiplied by the capital multiplier (territory DESIGN § 1, G11).</summary>
    public bool IsCapital { get; set; }
    [RelatedEntityField(typeof(Street))]
    public ICollection<Street> Streets { get; set; } = new Collection<Street>();
    [RelatedEntityField(typeof(Structure))]
    public ICollection<Structure> Structures { get; set; } = new Collection<Structure>();
}
