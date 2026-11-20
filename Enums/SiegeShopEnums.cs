namespace knkwebapi_v2.Enums;

// Siege shop catalogue (knk-workspace docs/specs/siege-shop/DESIGN.md § 5.3, § 5.4, § 6.1; PandiO/knk-workspace#867).
// Stored as strings (HasConversion<string>()) and serialized by name by the global JsonStringEnumConverter.

// P05: each mode keeps its own default list of shop entries.
public enum SiegeGameMode
{
    Siege,
    SiegeSurvival
}

// P19: what a shop entry sells. MagicAction comes with the magic design (not in v1).
public enum SiegeShopEntryKind
{
    Item,
    SiegeWeapon
}

// P06, § 5.4: how a price or cooldown step grows with the purchase number i (1, 2, 3, ...).
// None: s; Linear: s × i; Logarithmic: s × (1 + ln i); Exponential: s × f^(i − 1).
public enum SiegeShopCurve
{
    None,
    Linear,
    Logarithmic,
    Exponential
}
