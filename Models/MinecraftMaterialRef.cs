using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// The catalogue entry for one Minecraft namespaced key (KNG-200 D1: the entity keeps its name, table
/// and ids). A key can be an item, a block or both. The scan fields below are filled by the server scan
/// (phase 3); until then the backfill sets <see cref="DisplayName"/> and leaves the flags unknown.
/// </summary>
[FormConfigurableEntity("MinecraftMaterialRef")]
public class MinecraftMaterialRef
{
    public int Id { get; set; }

    [Required]
    [MaxLength(191)]
    public string NamespaceKey { get; set; } = null!;

    public string? LegacyName { get; set; }

    /// <summary>Legacy free-text category, kept for picker settings and seeds; not reliable.</summary>
    [Required]
    public string Category { get; set; } = null!;

    public string? IconUrl { get; set; }

    [MaxLength(191)]
    public string? DisplayName { get; set; }

    /// <summary>From the scan; null until a scan has seen this key.</summary>
    public bool? IsItem { get; set; }

    /// <summary>From the scan; null until a scan has seen this key.</summary>
    public bool? IsBlock { get; set; }

    public int? MaxStackSize { get; set; }

    /// <summary><c>BlockData.getAsString()</c> of the default state when <see cref="IsBlock"/>.</summary>
    public string? DefaultBlockState { get; set; }

    public MinecraftCatalogStatus CatalogStatus { get; set; } = MinecraftCatalogStatus.Unverified;

    [MaxLength(32)]
    public string? FirstSeenVersion { get; set; }

    [MaxLength(32)]
    public string? LastSeenVersion { get; set; }

    public int? LastScanId { get; set; }

    /// <summary>Manual icon; wins over <see cref="IconUrl"/> and is never overwritten by a scan.</summary>
    public string? IconUrlOverride { get; set; }

    /// <summary>Provider and version the icon came from, e.g. <c>prismarine@26.1</c>.</summary>
    [MaxLength(64)]
    public string? IconSource { get; set; }

    public MinecraftIconStatus? IconStatus { get; set; }
}
