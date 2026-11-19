using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// An optional governmental level between town and kingdom (territory DESIGN § 2): it groups towns of one kingdom. A
/// town in a province must be in the province's kingdom (checked when either side is saved). Its territory is the
/// combined area of its towns; it is not a holder of its own in V1.
/// </summary>
[FormConfigurableEntity("Province")]
public class Province
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    public int Id { get; set; }

    private string _name = null!;

    /// <summary>Unique within its kingdom (checked by the service).</summary>
    [MaxLength(NameMaxLength)]
    public string Name
    {
        get => _name;
        set => _name = value is null ? null! : value.Trim();
    }

    [MaxLength(DescriptionMaxLength)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NavigationPair(nameof(Kingdom))]
    [RelatedEntityField(typeof(Kingdom))]
    public int KingdomId { get; set; }

    [RelatedEntityField(typeof(Kingdom))]
    public Kingdom Kingdom { get; set; } = null!;

    [RelatedEntityField(typeof(Town))]
    public ICollection<Town> Towns { get; set; } = new List<Town>();
}
