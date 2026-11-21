using System;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A display spot of a shop (display stalls P12, PandiO/knk-workspace#250, shops DESIGN § 7 and § 9): a shelf or stall
/// block where the game server shows one of the shop's items as a Paper item display entity with a floating price label.
/// Spots are registered explicitly, like storage containers (production DESIGN § 6.2), never found by scanning: in game
/// with <c>/knk shop display add</c> while looking at the block, or in the web app.
/// <para>
/// One row per block (<see cref="WorldName"/>, <see cref="X"/>, <see cref="Y"/>, <see cref="Z"/>, unique). The item is shown
/// on top of the block. What a spot shows is chosen by the game server from the shop's offer: a spot's own pin
/// (<see cref="PinnedCatalogueEntryId"/>) first, then the catalogue's pinned entries, then in-stock items and best sellers
/// (<see cref="ShopTradeStat"/>), in <see cref="SortOrder"/>. Sold-out items are greyed out, or hidden when the shop's
/// <see cref="Shop.HideSoldOut"/> is set.
/// </para>
/// </summary>
[FormConfigurableEntity("ShopDisplaySpot")]
public class ShopDisplaySpot
{
    public const int WorldNameMaxLength = 64;
    public const int LabelMaxLength = 100;
    public const int MinY = -64;
    public const int MaxY = 320;
    /// <summary>At most this many spots per shop.</summary>
    public const int MaxPerShop = 100;

    public int Id { get; set; }

    [NavigationPair(nameof(Shop))]
    [RelatedEntityField(typeof(Shop))]
    public int ShopId { get; set; }
    [RelatedEntityField(typeof(Shop))]
    public Shop Shop { get; set; } = null!;

    [MaxLength(WorldNameMaxLength)]
    public string WorldName { get; set; } = null!;

    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>The way the item faces.</summary>
    public ShopDisplayFacing Facing { get; set; } = ShopDisplayFacing.South;

    /// <summary>A catalogue entry of the same shop this spot always shows (the admin's pin for this spot); null = chosen
    /// by the game server.</summary>
    [NavigationPair(nameof(PinnedCatalogueEntry))]
    [RelatedEntityField(typeof(ShopCatalogueEntry))]
    public int? PinnedCatalogueEntryId { get; set; }
    [RelatedEntityField(typeof(ShopCatalogueEntry))]
    public ShopCatalogueEntry? PinnedCatalogueEntry { get; set; }

    /// <summary>The order in which spots are filled (lowest first).</summary>
    public int SortOrder { get; set; }

    /// <summary>An optional name for staff ("Front shelf left").</summary>
    [MaxLength(LabelMaxLength)]
    public string? Label { get; set; }

    /// <summary>Off: nothing is shown on the spot.</summary>
    public bool Enabled { get; set; } = true;

    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
