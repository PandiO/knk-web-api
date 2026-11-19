using System;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("Structure")]
public class Structure : Domain
{
    // ===== Placement (production DESIGN § 3.3-3.4, PandiO/knk-workspace#91) =====
    // A structure is either in a district (DistrictId, usually with a street) or rural (TownId, no district and no
    // street). Exactly one of DistrictId and TownId is set (check constraint CK_structures_DistrictXorTown plus
    // StructurePlacement.Validate). Effective town = District.Town ?? Town (StructurePlacement.EffectiveTownId).

    [NavigationPair(nameof(Street))]
    [RelatedEntityField(typeof(Street))]
    public int? StreetId { get; set; }
    [RelatedEntityField(typeof(Street))]
    public Street? Street { get; set; }

    /// <summary>House number on <see cref="Street"/>; 0 for a structure without a street (rural).</summary>
    public int HouseNumber { get; set; }

    [NavigationPair(nameof(District))]
    [RelatedEntityField(typeof(District))]
    public int? DistrictId { get; set; }
    [RelatedEntityField(typeof(District))]
    public District? District { get; set; }

    /// <summary>The town of a rural structure (no district). Null for a structure in a district: its town is the
    /// district's.</summary>
    [NavigationPair(nameof(Town))]
    [RelatedEntityField(typeof(Town))]
    public int? TownId { get; set; }
    [RelatedEntityField(typeof(Town))]
    public Town? Town { get; set; }

    /// <summary>The structure's town: its district's town, or <see cref="TownId"/> for a rural structure. Needs
    /// <see cref="District"/> loaded for a structure in a district.</summary>
    public int? EffectiveTownId => District?.TownId ?? TownId;

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
