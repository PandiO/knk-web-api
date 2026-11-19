using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// The vision's "Resource-Property" (production DESIGN § 3.3; P2, PandiO/knk-workspace#91): one structure subtype for
/// every production kind (TPT, table <c>production_structures</c>); the kind is data (PRODUCTION_KINDS.md § 2). Mostly
/// rural (<see cref="Structure.TownId"/>). Gets its default storages on create (an Output storage until P3 seeds kinds
/// with their own storage lists). Production itself (lines, areas, cycles) comes with P3 and P4.
/// </summary>
[FormConfigurableEntity("ProductionStructure")]
public class ProductionStructure : Structure
{
    public const int MinLevel = 1;
    public const int MaxLevel = 10;
    public const int MaxWorkers = 100;

    /// <summary>The production kind (Farm, Logger, ...). A plain id until P3 adds the <c>ProductionKind</c> catalogue
    /// and its foreign key; null = not chosen yet.</summary>
    public int? ProductionKindId { get; set; }

    /// <summary>1-10 per kind (G8).</summary>
    public int Level { get; set; } = MinLevel;

    /// <summary>The structure's own switch: off = no production even with the feature on.</summary>
    public bool ProductionEnabled { get; set; } = true;

    // ---- Transport settings (DESIGN § 7.4) ----

    /// <summary>Plan transport to a warehouse automatically (held while <see cref="RoadConnection"/> is not Connected).</summary>
    public bool AutomaticTransport { get; set; } = true;

    /// <summary>Raw units in the output storage that trigger a transport; null = the planner's default.</summary>
    public int? TransportThreshold { get; set; }

    /// <summary>Raw units one transport carries; null = the planner's default.</summary>
    public int? TransportCapacity { get; set; }

    public WarehousePreference WarehousePreference { get; set; } = WarehousePreference.Nearest;

    /// <summary>Used with <see cref="WarehousePreference.Preferred"/>.</summary>
    [NavigationPair(nameof(PreferredWarehouse))]
    [RelatedEntityField(typeof(Warehouse))]
    public int? PreferredWarehouseId { get; set; }
    [RelatedEntityField(typeof(Warehouse))]
    public Warehouse? PreferredWarehouse { get; set; }

    /// <summary>Whether the structure reaches its town's road network (DESIGN § 7.6, D5); measured by the plugin (P5b).</summary>
    public RoadConnectionState RoadConnection { get; set; } = RoadConnectionState.Unmeasured;

    public DateTime? RoadConnectionMeasuredAt { get; set; }

    /// <summary>Workers the structure has (DESIGN § 7.7, default 2).</summary>
    public int WorkersTotal { get; set; } = 2;

    /// <summary>Workers present at the structure right now (the rest are carrying goods, § 7.7). Written by P3/P5.</summary>
    public int WorkersPresent { get; set; } = 2;
}
