using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("Town")]
public class Town : Domain
{
    /// <summary>
    /// PandiO/knk-workspace#409: the distinct streets of this town's districts, ordered by name (case-insensitive) then
    /// id. Read-only and not mapped: streets are linked on the district (<c>DistrictStreet</c>), so load
    /// <c>Districts.Streets</c> to fill it. Not usable in an EF query.
    /// </summary>
    [NotMapped]
    [RelatedEntityField(typeof(Street))]
    public IReadOnlyList<Street> Streets => Districts
        .SelectMany(d => d.Streets ?? Enumerable.Empty<Street>())
        .GroupBy(s => s.Id)
        .Select(g => g.First())
        .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(s => s.Id)
        .ToList();

    // ===== Territory (territory DESIGN § 1-2, PandiO/knk-workspace#326) =====

    /// <summary>The kingdom this town belongs to; null for an independent town.</summary>
    [NavigationPair(nameof(Kingdom))]
    [RelatedEntityField(typeof(Kingdom))]
    public int? KingdomId { get; set; }
    [RelatedEntityField(typeof(Kingdom))]
    public Kingdom? Kingdom { get; set; }

    /// <summary>Optional province; it must be in the same kingdom (checked on save).</summary>
    [NavigationPair(nameof(Province))]
    [RelatedEntityField(typeof(Province))]
    public int? ProvinceId { get; set; }
    [RelatedEntityField(typeof(Province))]
    public Province? Province { get; set; }

    /// <summary>Admin-set power (V1). Range = base range + power × factor (game settings, G11).</summary>
    public int Power { get; set; }

    // ===== Town logistics (PandiO/knk-workspace#928, TOWN_LOGISTICS § 4.1) =====

    /// <summary>The town's size label (Village, Town or City), the one the default policy templates key on; null = not
    /// set. Read and set through <c>api/LogisticsPolicies/towns/{townId}/profile</c>, not the town form.</summary>
    public knkwebapi_v2.Enums.LogisticsTownSize? LogisticsTownSize { get; set; }

    /// <summary>The town's specialization (a resource category, concept L144); null = general.</summary>
    public knkwebapi_v2.Enums.ResourceCategory? LogisticsSpecialization { get; set; }

    [NavigationPair(nameof(Districts))]
    [RelatedEntityField(typeof(District))]
    public ICollection<District> Districts { get; set; } = new Collection<District>();
}
