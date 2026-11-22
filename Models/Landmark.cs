using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// A named place made by staff, as a subtype of <see cref="Domain"/> (TPT, table <c>landmarks</c> keyed by the domain id;
/// knk-workspace <c>docs/specs/landmark-domain/DESIGN.md</c>, decisions LD01, LD02, LD11, LD16, LD17, PandiO/knk-workspace#983;
/// first built as a separate entity by off-road navigation phase 5.1, #708). The name, the pin
/// (<see cref="Domain.LocationId"/>, always set, a Location of its own), the created time and the region
/// (<see cref="Domain.WgRegionId"/>, only with <see cref="LandmarkAreaKind.OwnRegion"/>) live on the domain row.
/// A landmark is the one domain type that may have no region (LD11): its area is the pin, its own region with its
/// sub-regions, or the area of its parent domain (<see cref="AreaKind"/>). Every domain behaviour is off or neutral by
/// default (LD24). The name is at most 100 characters and unique per world among landmarks, checked by
/// <c>LandmarkService</c> (LD18).
/// </summary>
[FormConfigurableEntity("Landmark")]
public class Landmark : Domain
{
    public const int NameMaxLength = 100;
    public const int RegionIdMaxLength = 128;
    public const double PreferenceMin = 0.25;
    public const double PreferenceMax = 4.0;
    public const double DefaultPreference = 1.0;

    /// <summary>What the landmark's area is (LD16): the pin only, its own region and sub-regions, or the parent's area.</summary>
    public LandmarkAreaKind AreaKind { get; set; } = LandmarkAreaKind.None;

    /// <summary>
    /// The linked domain, the landmark's parent in the domain hierarchy (LD09, LD23): any domain, also another landmark,
    /// never the landmark itself or one of its descendants. No containment check. Null: top level. Cleared when the
    /// parent is deleted. Formerly <c>DomainId</c> (#708).
    /// </summary>
    [NavigationPair(nameof(ParentDomain))]
    [RelatedEntityField(typeof(Domain))]
    public int? ParentDomainId { get; set; }

    [RelatedEntityField(typeof(Domain))]
    public Domain? ParentDomain { get; set; }

    /// <summary>Cost multiplier of walk cells inside the area (off-road phase 5.2), 0.25 to 4.0; lower is preferred.</summary>
    public double Preference { get; set; } = DefaultPreference;

    /// <summary>False: the landmark exists but is not offered as a <c>/navigate</c> destination.</summary>
    public bool NavigationEnabled { get; set; } = true;

    /// <summary>The landmark kind (off-road DESIGN § 6.3): <c>General</c> only in v1; the plaza extension adds <c>Plaza</c>.</summary>
    public LandmarkKind Kind { get; set; } = LandmarkKind.General;

    /// <summary>
    /// LD05, LD15: the game server shows the enter and leave messages of the landmark's own region only when this is on
    /// (off by default). With <see cref="LandmarkAreaKind.LinkedDomain"/> the parent's messages apply.
    /// </summary>
    public bool ShowEnterLeaveMessages { get; set; }
}

/// <summary>LD16 <c>AreaKind</c>, stored as its name (replaces the off-road <c>ExtentKind</c>, whose Radius went with LD08).</summary>
public enum LandmarkAreaKind
{
    /// <summary>The pin only: arrival within the arrival distance, no region, no sub-regions, no access flags.</summary>
    None,

    /// <summary>The landmark owns <see cref="Domain.WgRegionId"/> and its sub-regions, under the one-owner rule.</summary>
    OwnRegion,

    /// <summary>The parent's main region and sub-regions; the landmark owns no region. Needs a parent with a region.</summary>
    LinkedDomain,
}

/// <summary>Off-road DESIGN § 6.3 <c>Kind</c>, stored as its name; <c>Plaza</c> follows with § 10.1.</summary>
public enum LandmarkKind
{
    General,
}
