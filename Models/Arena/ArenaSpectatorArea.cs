using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// Where players watch an Arena's duels (arena duels DESIGN.md §4.1, §9.3): an ordinary walkable Domain, a /warp
/// destination through the inherited warp fields (Location, TeleportEnabled, price, title, premium, discovery).
/// 0..n per arena, normally one.
/// </summary>
[FormConfigurableEntity("ArenaSpectatorArea")]
public class ArenaSpectatorArea : Domain
{
    [NavigationPair(nameof(Arena))]
    [RelatedEntityField(typeof(Arena))]
    public int ArenaId { get; set; }
    [RelatedEntityField(typeof(Arena))]
    public Arena Arena { get; set; } = null!;
}
