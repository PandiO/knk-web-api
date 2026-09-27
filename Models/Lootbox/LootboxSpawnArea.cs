using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// Where boxes may spawn (DESIGN.md §3.2, D17): a WorldGuard region plus spawn limits. Created in the web app or
/// in game (<c>/knk lootbox area create</c>, which sets <see cref="CreatedByUserId"/>); limits are tuned in the web
/// app. <see cref="Name"/> is unique (<c>[A-Za-z0-9_-]{1,32}</c>) because the in-game command addresses areas by
/// name. Defaults echo v1's loot ocelots (every 600 s, at least 3 players online).
/// </summary>
[FormConfigurableEntity("LootboxSpawnArea")]
public class LootboxSpawnArea
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string World { get; set; } = null!;
    public string WgRegionId { get; set; } = null!;
    public bool Enabled { get; set; }

    public int MaxActive { get; set; } = 3;
    public int SpawnIntervalSeconds { get; set; } = 600;
    // Percent, 0-100.
    public decimal SpawnChancePercent { get; set; } = 100m;
    public int MinOnlinePlayers { get; set; } = 3;
    public int MinDistanceFromPlayers { get; set; } = 24;
    public int LifetimeMinutes { get; set; } = 30;

    // Comma-separated WG region ids boxes must stay out of (plots, structures).
    public string? ExcludedRegionIds { get; set; }

    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    // Allowed types; empty = every enabled type.
    [RelatedEntityField(typeof(LootboxSpawnAreaType))]
    public ICollection<LootboxSpawnAreaType> AllowedTypes { get; set; } = new List<LootboxSpawnAreaType>();
}
