using System;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("GateBlockSnapshot")]
public class GateBlockSnapshot
{
    public int Id { get; set; }

    // Foreign key to parent GateDoor (moved from GateStructure by item 5's multi-door support -
    // see GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md - a scan targets one door, not a whole structure)
    [RelatedEntityField(typeof(GateDoor))]
    public int GateDoorId { get; set; }

    // Position relative to AnchorPoint
    public int RelativeX { get; set; }
    public int RelativeY { get; set; }
    public int RelativeZ { get; set; }

    // World coordinates (for faster lookup)
    public int WorldX { get; set; }
    public int WorldY { get; set; }
    public int WorldZ { get; set; }

    // Material information
    [MaxLength(191)]
    [Required]
    public string MaterialName { get; set; } = null!;

    // Block state data (JSON)
    [MaxLength(1000)]
    public string BlockDataJson { get; set; } = "{}";

    // Tile entity data (JSON) - for chests, signs, etc.
    [MaxLength(2000)]
    public string TileEntityJson { get; set; } = "{}";

    // Animation sequence order (hinge → outward)
    public int SortOrder { get; set; }

    // Navigation property
    [RelatedEntityField(typeof(GateDoor))]
    public virtual GateDoor GateDoor { get; set; } = null!;
}
