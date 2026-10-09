namespace knkwebapi_v2.Enums;

/// <summary>
/// How a permission group prices one kind of teleport (Linear KNG-41, teleport
/// IMPLEMENTATION_PLAN.md "KNG-41"). Stored as an INT; never renumber.
/// </summary>
public enum TeleportPriceMode
{
    /// <summary>The group sets no price: the next group in the player's chain decides, else the default
    /// (the plugin's teleport.request.price-coins for /tpa, the domain's gem price for /warp, free /spawn).</summary>
    None = 0,

    /// <summary>An exact price in coins and/or gems and/or XP, replacing the default (all zero = free).</summary>
    Fixed = 1,

    /// <summary>The default price times a factor (0.5 = half, 0 = free), rounded half away from zero. A
    /// /spawn has no default price, so it can't use this mode.</summary>
    Multiplier = 2
}
