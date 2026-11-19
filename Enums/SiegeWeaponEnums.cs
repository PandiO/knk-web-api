namespace knkwebapi_v2.Enums;

// Siege weapons (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 3, § 9; PandiO/knk-workspace#782).
// Stored as strings (HasConversion<string>()) and serialized by name by the global JsonStringEnumConverter.

// W16, W24: indirect weapons (trebuchet, catapult) are aimed at a map target; direct ones (ballista) are mounted.
public enum SiegeWeaponKind
{
    Indirect,
    Direct
}

// W3: a spot holds a weapon of its own class or a smaller one (§ 3 size class table).
public enum SiegeWeaponSizeClass
{
    Small,
    Medium,
    Large
}

// W48, W49: sets the vanilla damage type and the fall-off curve of an ammo type.
public enum SiegeAmmoEffect
{
    Impact,
    Explosion,
    Fire,
    Poison
}

// W5, § 4.4: a spot's facing, rounded to the four directions the 90° schematic rotation supports.
public enum SiegeWeaponFacing
{
    North,
    East,
    South,
    West
}
