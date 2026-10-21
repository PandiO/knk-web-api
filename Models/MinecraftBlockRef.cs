using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// A reusable block state of a <see cref="MinecraftMaterialRef"/> (KNG-200 §2.2). Identity is
/// <see cref="StateKey"/>: the hash of the normalised key and the canonical state.
/// </summary>
[FormConfigurableEntity("MinecraftBlockRef")]
public class MinecraftBlockRef
{
    public int Id { get; set; }

    /// <summary>Copy of the material's key, kept for compatibility.</summary>
    [Required]
    [MaxLength(191)]
    public string NamespaceKey { get; set; } = null!;

    /// <summary>Canonical state, e.g. <c>minecraft:oak_stairs[facing=north,half=bottom]</c>; null means any state.</summary>
    public string? BlockStateString { get; set; }

    public string? LogicalType { get; set; }

    public string? IconUrl { get; set; }

    /// <summary>The material this state belongs to; nullable until the backfill has linked every row.</summary>
    public int? MaterialRefId { get; set; }

    public MinecraftMaterialRef? MaterialRef { get; set; }

    /// <summary>SHA-256 (hex) of the normalised key and canonical state; see <c>MinecraftBlockState.StateKey</c>.</summary>
    [MaxLength(64)]
    public string? StateKey { get; set; }

    /// <summary>True when the state names fewer properties than the block has (KNG-200 D2); null until known from a scan.</summary>
    public bool? IsPartial { get; set; }
}
