using System;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// A named place made by staff (off-road navigation phase 5.1, PandiO/knk-workspace#708; knk-workspace
/// <c>docs/specs/navigation-offroad/DESIGN.md</c> § 6, decisions O04, O05, O26, O27): a <see cref="Location"/> pin with a
/// name, optionally linked to one domain and optionally with an extent (a WorldGuard region or a radius around the pin).
/// It answers "where is the throne room" for <c>/navigate</c> (through the destination engine's landmark provider) and,
/// from phase 5.2, for the walk planner. It is not a domain: no region, access rules or discovery of its own.
/// The name is unique per world (the world of its Location), checked by <c>LandmarkService</c>.
/// </summary>
[FormConfigurableEntity("Landmark")]
public class Landmark
{
    public const int NameMaxLength = 100;
    public const int RegionIdMaxLength = 128;
    public const double PreferenceMin = 0.25;
    public const double PreferenceMax = 4.0;
    public const double DefaultPreference = 1.0;

    public int Id { get; set; }

    private string _name = null!;

    /// <summary>Trimmed on every write, like <see cref="Domain.Name"/>: a trailing newline would reach the game server.</summary>
    [MaxLength(NameMaxLength)]
    public string Name
    {
        get => _name;
        set => _name = value is null ? null! : value.Trim();
    }

    /// <summary>The pin: the landmark's point and world. Reuses the Location entity and its retention rules.</summary>
    [NavigationPair(nameof(Location))]
    [RelatedEntityField(typeof(Location))]
    public int LocationId { get; set; }

    [RelatedEntityField(typeof(Location))]
    public Location Location { get; set; } = null!;

    /// <summary>The domain it belongs to (any domain: town, district, structure, gate); null when none. Cleared when the domain is deleted.</summary>
    [NavigationPair(nameof(Domain))]
    [RelatedEntityField(typeof(Domain))]
    public int? DomainId { get; set; }

    [RelatedEntityField(typeof(Domain))]
    public Domain? Domain { get; set; }

    /// <summary>How arrival is measured: within <c>arrive-distance</c> of the pin, inside <see cref="RegionId"/>, or within <see cref="Radius"/>.</summary>
    public LandmarkExtentKind ExtentKind { get; set; } = LandmarkExtentKind.None;

    /// <summary>
    /// The WorldGuard region id when <see cref="ExtentKind"/> is <c>Region</c>; null otherwise. Either an existing region
    /// (kept as it is) or one drawn through the form's region task, which is renamed from
    /// <c>tempregion_worldtask_&lt;n&gt;</c> to <c>landmark_&lt;id&gt;</c> on save (PandiO/knk-workspace#945).
    /// </summary>
    [MaxLength(RegionIdMaxLength)]
    public string? RegionId { get; set; }

    /// <summary>Blocks around the pin (a cylinder) when <see cref="ExtentKind"/> is <c>Radius</c>; null otherwise.</summary>
    public double? Radius { get; set; }

    /// <summary>Cost multiplier of walk cells inside the extent (phase 5.2), 0.25 to 4.0; lower is preferred.</summary>
    public double Preference { get; set; } = DefaultPreference;

    /// <summary>False: the landmark exists but is not offered as a <c>/navigate</c> destination.</summary>
    public bool NavigationEnabled { get; set; } = true;

    /// <summary>The discriminator (DESIGN § 6.3): <c>General</c> only in v1; the plaza extension (§ 10.1) adds <c>Plaza</c>.</summary>
    public LandmarkKind Kind { get; set; } = LandmarkKind.General;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Off-road DESIGN § 6.3 <c>ExtentKind</c>, stored as its name.</summary>
public enum LandmarkExtentKind
{
    None,
    Region,
    Radius,
}

/// <summary>Off-road DESIGN § 6.3 <c>Kind</c>, stored as its name; <c>Plaza</c> follows with § 10.1.</summary>
public enum LandmarkKind
{
    General,
}
