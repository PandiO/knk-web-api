using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// A kingdom, a territory holder above towns (territory DESIGN § 2, PandiO/knk-workspace#326). It has no region and no
/// border of its own: its territory is its towns' territories plus its crown land. Towns link to it
/// (<see cref="Town.KingdomId"/>), provinces belong to it, and it owns its <see cref="CrownLand"/>.
/// </summary>
[FormConfigurableEntity("Kingdom")]
public class Kingdom
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    public int Id { get; set; }

    private string _name = null!;

    /// <summary>Unique (checked by the service); trimmed on every write like <see cref="Domain.Name"/>.</summary>
    [MaxLength(NameMaxLength)]
    public string Name
    {
        get => _name;
        set => _name = value is null ? null! : value.Trim();
    }

    [MaxLength(DescriptionMaxLength)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [RelatedEntityField(typeof(Town))]
    public ICollection<Town> Towns { get; set; } = new List<Town>();

    [RelatedEntityField(typeof(Province))]
    public ICollection<Province> Provinces { get; set; } = new List<Province>();

    [RelatedEntityField(typeof(CrownLand))]
    public ICollection<CrownLand> CrownLands { get; set; } = new List<CrownLand>();
}
