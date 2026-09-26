namespace knkwebapi_v2.Enums;

// Lootbox enums (knk-workspace docs/specs/lootboxes/DESIGN.md §3.2). All stored as their string names.

/// <summary>Lifecycle of a world box. <see cref="Active"/> is the only claimable state.</summary>
public enum LootboxSpawnStatus
{
    Active = 0,
    Claimed = 1,
    Expired = 2,
    Removed = 3
}

/// <summary>A <see cref="knkwebapi_v2.Models.LootboxPoolEntry"/> either adds a blueprint to a type's pool or removes one.</summary>
public enum LootboxPoolMode
{
    Include = 0,
    Exclude = 1
}

/// <summary>How the plugin handed a claimed item over (DESIGN.md §3.4).</summary>
public enum LootboxDeliveryMethod
{
    Inventory = 0,
    DroppedOwned = 1,
    Redelivered = 2
}
