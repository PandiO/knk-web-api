using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// Kingdom-held land such as a frontier fort or a royal forest (territory DESIGN § 2, decision G10): a domain with a
/// WorldGuard region, created like a district through the form and the world-task region capture, owned by a
/// <see cref="Kingdom"/>. Its territory is computed by the game server from the distance to the nearest edge of its
/// region against <see cref="Range"/> (G11, no capital multiplier) and outranks town territory where they overlap.
/// </summary>
[FormConfigurableEntity("CrownLand")]
public class CrownLand : Domain
{
    public const int MaxRange = 10_000;

    [NavigationPair(nameof(Kingdom))]
    [RelatedEntityField(typeof(Kingdom))]
    public int KingdomId { get; set; }

    [RelatedEntityField(typeof(Kingdom))]
    public Kingdom Kingdom { get; set; } = null!;

    /// <summary>Blocks from the nearest edge of the region that still count as this crown land (0 = the region only).</summary>
    public int Range { get; set; }
}
