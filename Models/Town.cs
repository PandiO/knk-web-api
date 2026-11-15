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

    [NavigationPair(nameof(Districts))]
    [RelatedEntityField(typeof(District))]
    public ICollection<District> Districts { get; set; } = new Collection<District>();
}
