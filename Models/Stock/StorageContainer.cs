using System;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A block in the world bound to a storage (P6a, PandiO/knk-workspace#683, production DESIGN § 6.2, decision record
/// § 2.3). Containers are registered explicitly (<c>/knk storage bind</c> in game, or the web app), never found by
/// scanning. The API is the only truth: a <see cref="StorageContainerMode.Terminal"/> block shows the storage as a paged
/// menu and take/put are ledger moves (the block stays empty); a <see cref="StorageContainerMode.Display"/> block is
/// filled read-only with a sample of the stock. Backed mode is Later (#113).
/// <para>
/// One row per block (<see cref="WorldName"/>, <see cref="X"/>, <see cref="Y"/>, <see cref="Z"/>, unique). A double
/// chest is keyed by its canonical half (the plugin sends the left half, DESIGN § 6.3) with <see cref="IsDouble"/> set,
/// so it is never counted twice. Every bound container adds its <see cref="Slots"/> to the storage's container-derived
/// cap: <c>MaxUnits = Σ slots × UnitsPerSlot</c> (DESIGN § 4.4), written when a container is bound, changed or unbound.
/// </para>
/// </summary>
[FormConfigurableEntity("StorageContainer")]
public class StorageContainer
{
    public const int WorldNameMaxLength = 64;
    public const int LabelMaxLength = 100;
    public const int SingleSlots = 27;
    public const int DoubleSlots = 54;

    public int Id { get; set; }

    [NavigationPair(nameof(Storage))]
    [RelatedEntityField(typeof(Storage))]
    public int StorageId { get; set; }
    [RelatedEntityField(typeof(Storage))]
    public Storage Storage { get; set; } = null!;

    public StorageContainerMode Mode { get; set; } = StorageContainerMode.Terminal;

    public StorageContainerType ContainerType { get; set; } = StorageContainerType.Chest;

    /// <summary>A double chest (chests and trapped chests only); the row is the canonical half.</summary>
    public bool IsDouble { get; set; }

    /// <summary>Inventory slots of the block: 27, or 54 for a double chest. Derived from the type on save.</summary>
    public int Slots { get; set; } = SingleSlots;

    [MaxLength(WorldNameMaxLength)]
    public string WorldName { get; set; } = null!;

    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>An optional name for staff ("North terminal").</summary>
    [MaxLength(LabelMaxLength)]
    public string? Label { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public static int SlotsFor(StorageContainerType type, bool isDouble) =>
        isDouble && type != StorageContainerType.Barrel ? DoubleSlots : SingleSlots;
}
