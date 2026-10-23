using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// The catalogue entry for one enchantment (KNG-250): vanilla ones from the server's enchantment registry,
/// custom ones (<c>knk:&lt;id&gt;</c>) from knk-plugin's EnchantmentRegistry, both filled by the server scan.
/// </summary>
[FormConfigurableEntity("MinecraftEnchantmentRef")]
public class MinecraftEnchantmentRef
{
    public int Id { get; set; }

    [Required]
    [MaxLength(191)]
    public string NamespaceKey { get; set; } = string.Empty;

    public string? LegacyName { get; set; }

    /// <summary>Legacy category (Weapon, Armor, ...) for vanilla rows; ATTACK/SUPPORT for custom ones.</summary>
    public string? Category { get; set; }

    public string? IconUrl { get; set; }

    public int? MaxLevel { get; set; }

    public string? DisplayName { get; set; }

    public bool IsCustom { get; set; } = false;

    public MinecraftCatalogStatus CatalogStatus { get; set; } = MinecraftCatalogStatus.Unverified;

    [MaxLength(32)]
    public string? FirstSeenVersion { get; set; }

    [MaxLength(32)]
    public string? LastSeenVersion { get; set; }

    public int? LastScanId { get; set; }
}
