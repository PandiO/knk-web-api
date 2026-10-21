using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// A fighting area of an Arena (arena duels DESIGN.md §4.1): a Domain whose WgRegionId is the battleground's region.
/// Entry is off by default; the duel runtime lets the two duelists in through a domain-access exemption (D16, §8.2).
/// </summary>
[FormConfigurableEntity("ArenaBattleground")]
public class ArenaBattleground : Domain
{
    public ArenaBattleground()
    {
        AllowEntry = false;
    }

    [NavigationPair(nameof(Arena))]
    [RelatedEntityField(typeof(Arena))]
    public int ArenaId { get; set; }
    [RelatedEntityField(typeof(Arena))]
    public Arena Arena { get; set; } = null!;

    /// <summary>Unique within the arena (e.g. "bg1").</summary>
    public string Key { get; set; } = null!;

    /// <summary>Allocation order.</summary>
    public int SortOrder { get; set; }

    /// <summary>Off = never allocated. Needs both spawns to be on.</summary>
    public bool IsEnabled { get; set; } = true;

    [NavigationPair(nameof(SpawnALocation))]
    [RelatedEntityField(typeof(Location))]
    public int? SpawnALocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location? SpawnALocation { get; set; }

    [NavigationPair(nameof(SpawnBLocation))]
    [RelatedEntityField(typeof(Location))]
    public int? SpawnBLocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location? SpawnBLocation { get; set; }
}
