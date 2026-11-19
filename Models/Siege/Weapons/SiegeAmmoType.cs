using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// One ammunition type (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 3, § 9.2; W19). Heavy and light
// variants are separate rows. Templates list the types they may fire through SiegeWeaponTemplateAmmoType.
[FormConfigurableEntity("SiegeAmmoType")]
public class SiegeAmmoType
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public SiegeAmmoEffect Effect { get; set; } = SiegeAmmoEffect.Impact;   // W48, W49

    public double Damage { get; set; } = 10;             // player damage at the centre (W48)
    public double StructureDamage { get; set; } = 100;   // gate HP units against gates and weapons (W65)
    public double BlastRadius { get; set; } = 2;         // blocks (W18)
    public double Knockback { get; set; } = 1;           // strength, same fall-off as the damage (W47)
    public double ArmorPiercing { get; set; }            // 0-1, share of the armour reduction ignored (W49)

    // Projectile look (W54): the block shown by the display entity and the particle of its trail.
    public string? ProjectileBlock { get; set; }
    public string? ParticleTrail { get; set; }

    // Fire area for fire bombs, poison level and duration for carcasses (§ 3).
    public double? EffectRadius { get; set; }
    public double? EffectDurationSeconds { get; set; }
    public int? EffectLevel { get; set; }

    public int PricePerShot { get; set; } = 10;          // siege points, paid by whoever fires (W20)
}
