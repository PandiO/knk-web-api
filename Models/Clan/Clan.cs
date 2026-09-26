using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// Minimal clan (vision §3.2, docs/specs/siege-minigame/DESIGN.md §3.2 / D4): identity only - name,
// colour, banner, and optionally "the default clan of this town" (unique per town). No membership,
// ranks, diplomacy or ownership yet. Siege teams reference a Clan for their identity (Phase 2).
[FormConfigurableEntity("Clan")]
public class Clan
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    // Baseline crown/garrison clan rather than a player clan.
    public bool IsNpc { get; set; }

    // Bukkit ChatColor colour name (BLACK .. WHITE), same convention as MenuItemTemplate.ChatColorName.
    public string ChatColor { get; set; } = "WHITE";

    // Restrict: a BannerDesign still used by a Clan can't be deleted out from under it.
    [RelatedEntityField(typeof(BannerDesign))]
    [NavigationPair("BannerDesign")]
    public int BannerDesignId { get; set; }
    [RelatedEntityField(typeof(BannerDesign))]
    public BannerDesign BannerDesign { get; set; } = null!;

    // Unique when set (a town has at most one default clan); Restrict.
    [RelatedEntityField(typeof(Town))]
    [NavigationPair("DefaultForTown")]
    public int? DefaultForTownId { get; set; }
    [RelatedEntityField(typeof(Town))]
    public Town? DefaultForTown { get; set; }
}
