using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// One siege weapon type (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 3, § 9.1; PandiO/knk-workspace#782).
// Configuration only: placed weapons, HP, reload timers and leases are the plugin's match state (W63).
// Defaults are the spec's proposals, tuned in the alpha balance runs (§ 15.2).
[FormConfigurableEntity("SiegeWeaponTemplate")]
public class SiegeWeaponTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public SiegeWeaponKind Kind { get; set; } = SiegeWeaponKind.Indirect;
    public SiegeWeaponSizeClass SizeClass { get; set; } = SiegeWeaponSizeClass.Medium;

    // Armed, fired and wreck schematics (W2, W8, W55) from the schematic pipeline (PandiO/knk-workspace#614). That
    // pipeline has no table yet, so these are plain nullable ids without a foreign key until it exists.
    public int? ArmedSchematicId { get; set; }
    public int? FiredSchematicId { get; set; }
    public int? WreckSchematicId { get; set; }

    public int MaxHealth { get; set; } = 300;            // W6, HP at the end of the build
    public int BuildTimeSeconds { get; set; } = 60;      // W7
    public int ReloadSeconds { get; set; } = 20;         // W21

    public double MaxRange { get; set; } = 100;          // W22, blocks from the spot
    public double? MinRange { get; set; }                // W22, optional

    // Indirect only (W58): targeting circle radius at the first shot, its floor, and the step each consecutive shot at
    // about the same target takes off.
    public double StartSpreadRadius { get; set; } = 8;
    public double MinSpreadRadius { get; set; } = 3;
    public double RangingStep { get; set; } = 1.5;

    // Indirect only (W66): flight time at max range (shorter shots scale with distance, never below 1.5 s) and the
    // arc's height in blocks.
    public double FlightTimeAtMaxRangeSeconds { get; set; } = 4;
    public double ArcHeight { get; set; } = 20;

    // W71: seconds without a shot before the lease ends; null = the global default.
    public int? LeaseIdleTimeoutSeconds { get; set; }

    // Direct only (W24, W52): half-angle of the firing cone around the spot's facing.
    public double ConeHalfAngleDegrees { get; set; } = 60;

    // W25, W65: factors on an ammo type's structure damage against gates and enemy siege weapons.
    public double GateDamageMultiplier { get; set; } = 1;
    public double WeaponDamageMultiplier { get; set; } = 1;

    public int SpotCooldownSeconds { get; set; } = 120;  // W10, W11; the scenario may override it
    public int MatchPrice { get; set; } = 200;           // W13, siege points to place one
    public int UnlockPrice { get; set; } = 1000;         // W32, personal coins to unlock the type

    // W19: M2M join; the join rows cascade with the template, an ammo type in use can't be deleted.
    [RelatedEntityField(typeof(SiegeWeaponTemplateAmmoType))]
    public ICollection<SiegeWeaponTemplateAmmoType> AllowedAmmoTypes { get; set; } = new List<SiegeWeaponTemplateAmmoType>();
}
