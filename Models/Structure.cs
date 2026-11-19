using System;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("Structure")]
public class Structure : Domain
{
    [NavigationPair(nameof(Street))]
    [RelatedEntityField(typeof(Street))]
    public int StreetId { get; set; }
    [RelatedEntityField(typeof(Street))]
    public Street Street { get; set; } = null!;
    public int HouseNumber { get; set; }
    
    [NavigationPair(nameof(District))]
    [RelatedEntityField(typeof(District))]
    public int DistrictId { get; set; }
    [RelatedEntityField(typeof(District))]
    public District District { get; set; } = null!;

    // ===== Territory (territory DESIGN § 4, PandiO/knk-workspace#326) =====

    /// <summary>The town the structure is loyal to, separate from ownership; null = loyal to its own town. Stored only
    /// in V1 (switching sides and revolts come later); set by admins.</summary>
    [NavigationPair(nameof(LoyaltyTown))]
    [RelatedEntityField(typeof(Town))]
    public int? LoyaltyTownId { get; set; }
    [RelatedEntityField(typeof(Town))]
    public Town? LoyaltyTown { get; set; }

    /// <summary>Set by the game server's territory report when the structure's land is contested or outside its town's
    /// territory. The structure keeps its town (territory DESIGN § 4).</summary>
    public StructureTerritoryFlag TerritoryFlag { get; set; } = StructureTerritoryFlag.None;

    public DateTime? TerritoryFlaggedAt { get; set; }

    /// <summary>The conflict the structure is listed on when <see cref="TerritoryFlag"/> is Contested.</summary>
    [NavigationPair(nameof(TerritoryConflict))]
    [RelatedEntityField(typeof(TerritoryConflict))]
    public int? TerritoryConflictId { get; set; }
    [RelatedEntityField(typeof(TerritoryConflict))]
    public TerritoryConflict? TerritoryConflict { get; set; }
}
