using System;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// An extra WorldGuard region of a domain besides its main <see cref="Domain.WgRegionId"/> (an expansion region, KNG-254):
/// a garrison gate's cylindrical tower next to its house, a town's annex. The domain is the union of its regions. The game
/// server keeps every sub-region a copy of the main region (priority, parent, flags, owners, members), so admins only ever
/// edit the main region. Drawn through a world task as <c>tempregion_worldtask_&lt;n&gt;</c> and renamed to
/// <c>domain_&lt;domainId&gt;_sub_&lt;id&gt;</c> when saved. Lives in its domain's world. See knk-workspace
/// <c>docs/specs/domain-subregions/DESIGN.md</c>.
/// </summary>
[FormConfigurableEntity("DomainSubRegion")]
public class DomainSubRegion
{
    public int Id { get; set; }

    [NavigationPair(nameof(Domain))]
    [RelatedEntityField(typeof(Domain))]
    public int DomainId { get; set; }

    [RelatedEntityField(typeof(Domain))]
    public Domain Domain { get; set; } = null!;

    /// <summary>The WorldGuard region id; unique per world across every domain's main region and sub-regions.</summary>
    [MaxLength(128)]
    public string WgRegionId { get; set; } = null!;

    /// <summary>Optional label, such as "Tower" or "House".</summary>
    [MaxLength(64)]
    public string? Name { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
