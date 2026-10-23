using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// The navigation compass settings (KNG-234, docs/specs/navigation-compass/DESIGN.md): which item works as
/// the compass in game, and where. A single row (<see cref="SingletonId"/>); a database without it answers
/// the defaults, so nothing is seeded. The game server binds the behaviour to any item whose material and
/// plain display name match, so an admin can hand out the item from an item blueprint and it works at once.
/// </summary>
public class NavigationCompassSettings
{
    public const int SingletonId = 1;

    public const int MaxMaterialLength = 100;
    public const int MaxDisplayNameLength = 100;
    public const int MaxTargetsLength = 4000;
    public const int MaxTargets = 50;
    public const int MaxTargetLength = 100;

    public const string DefaultMaterial = "COMPASS";
    public const string DefaultDisplayName = "Navigation Compass";

    public int Id { get; set; } = SingletonId;

    /// <summary>Master switch: false means no item is a compass.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The item's material, e.g. COMPASS or minecraft:recovery_compass.</summary>
    public string Material { get; set; } = DefaultMaterial;

    /// <summary>The display name to match, colour codes ignored; empty means "any name" (material only).</summary>
    public string DisplayName { get; set; } = DefaultDisplayName;

    public NavigationCompassNameMatch NameMatch { get; set; } = NavigationCompassNameMatch.Exact;

    /// <summary>The compass works in a running siege match.</summary>
    public bool SiegeEnabled { get; set; } = true;

    /// <summary>The compass works outside a siege, cycling <see cref="GeneralTargets"/>.</summary>
    public bool GeneralEnabled { get; set; }

    /// <summary>Destinations outside a siege, one <c>/navigate</c> argument per line (town:Kardenna), in cycle order.</summary>
    public string GeneralTargets { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
