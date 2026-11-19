using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// SiegeWeaponTemplate <-> SiegeAmmoType M2M join (SIEGE_WEAPONS § 3 "Allowed ammo types", W19), composite key like
// SiegeScenarioDistrict. The join row cascades with its template; an ammo type a template allows can't be deleted
// (Restrict), so removing it from the templates is a deliberate step.
[FormConfigurableEntity("SiegeWeaponTemplateAmmoType")]
public class SiegeWeaponTemplateAmmoType
{
    [NavigationPair(nameof(SiegeWeaponTemplate))]
    [RelatedEntityField(typeof(SiegeWeaponTemplate))]
    public int SiegeWeaponTemplateId { get; set; }
    [RelatedEntityField(typeof(SiegeWeaponTemplate))]
    public SiegeWeaponTemplate SiegeWeaponTemplate { get; set; } = null!;

    [NavigationPair(nameof(SiegeAmmoType))]
    [RelatedEntityField(typeof(SiegeAmmoType))]
    public int SiegeAmmoTypeId { get; set; }
    [RelatedEntityField(typeof(SiegeAmmoType))]
    public SiegeAmmoType SiegeAmmoType { get; set; } = null!;
}
