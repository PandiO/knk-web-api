using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// A player's permanent unlock of one siege weapon type (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 7.1,
// § 9.6; W13, W32): needed to buy that weapon in a match, never to operate one. One row per user and template. Users
// are soft-deleted (Restrict, like UserDomainDiscovery); a template a player unlocked can't be deleted (Restrict),
// because the unlock was paid with coins.
[FormConfigurableEntity("UserSiegeWeaponUnlock")]
public class UserSiegeWeaponUnlock
{
    public int Id { get; set; }

    [NavigationPair(nameof(User))]
    [RelatedEntityField(typeof(User))]
    public int UserId { get; set; }
    [RelatedEntityField(typeof(User))]
    public User User { get; set; } = null!;

    [NavigationPair(nameof(SiegeWeaponTemplate))]
    [RelatedEntityField(typeof(SiegeWeaponTemplate))]
    public int SiegeWeaponTemplateId { get; set; }
    [RelatedEntityField(typeof(SiegeWeaponTemplate))]
    public SiegeWeaponTemplate SiegeWeaponTemplate { get; set; } = null!;

    public DateTime UnlockedAt { get; set; }
}
