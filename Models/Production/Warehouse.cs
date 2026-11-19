using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// A town's storage hub (production DESIGN § 3.3, TOWN_LOGISTICS.md; P2, PandiO/knk-workspace#91). A structure subtype
/// (TPT, table <c>warehouses</c>): in a district or rural like any structure. Gets a default delivery storage on create
/// (<see cref="DefaultStorageId"/>, a <c>Domain</c> storage held by the warehouse).
/// </summary>
[FormConfigurableEntity("Warehouse")]
public class Warehouse : Structure
{
    public const int MaxPriority = 1000;

    /// <summary>Takes deliveries from producers and transport (P5, P9).</summary>
    public bool AcceptsDeliveries { get; set; } = true;

    /// <summary>Higher first when several warehouses of a town could take a delivery (0-1000).</summary>
    public int Priority { get; set; }

    /// <summary>The storage deliveries go to; created with the warehouse (Purpose Delivery). Null when it was deleted.</summary>
    [NavigationPair(nameof(DefaultStorage))]
    [RelatedEntityField(typeof(Storage))]
    public int? DefaultStorageId { get; set; }
    [RelatedEntityField(typeof(Storage))]
    public Storage? DefaultStorage { get; set; }
}
