namespace knkwebapi_v2.Models;

/// <summary>
/// Armor trim pattern and material keys accepted for <see cref="ItemBlueprint.ArmorTrimPattern"/> /
/// <see cref="ItemBlueprint.ArmorTrimMaterial"/> (PandiO/knk-workspace#232) - mirrors the Bukkit
/// <c>trim_pattern</c> and <c>trim_material</c> registries of the server version knk-plugin compiles against
/// (paper-api 26.3), the way <see cref="BannerPatternKeys"/> mirrors the banner patterns. When Mojang adds a trim,
/// add its key here; knk-plugin's <c>ItemAppearanceBukkitMapper</c> skips (and logs) a key its running server
/// doesn't know.
/// </summary>
public static class ArmorTrimKeys
{
    public const string Namespace = "minecraft";

    public static readonly IReadOnlyList<string> Patterns = new[]
    {
        "minecraft:sentry",
        "minecraft:dune",
        "minecraft:coast",
        "minecraft:wild",
        "minecraft:ward",
        "minecraft:eye",
        "minecraft:vex",
        "minecraft:tide",
        "minecraft:snout",
        "minecraft:rib",
        "minecraft:spire",
        "minecraft:wayfinder",
        "minecraft:shaper",
        "minecraft:silence",
        "minecraft:raiser",
        "minecraft:host",
        "minecraft:flow",
        "minecraft:bolt",
    };

    public static readonly IReadOnlyList<string> Materials = new[]
    {
        "minecraft:quartz",
        "minecraft:iron",
        "minecraft:netherite",
        "minecraft:redstone",
        "minecraft:copper",
        "minecraft:gold",
        "minecraft:emerald",
        "minecraft:diamond",
        "minecraft:lapis",
        "minecraft:amethyst",
        "minecraft:resin",
    };

    private static readonly HashSet<string> KnownPatterns = new(Patterns, StringComparer.Ordinal);
    private static readonly HashSet<string> KnownMaterials = new(Materials, StringComparer.Ordinal);

    /// <summary>"Silence", "silence" or "minecraft:silence" to "minecraft:silence"; null when unknown or blank.</summary>
    public static string? NormalizePattern(string? key) => Normalize(key, KnownPatterns);

    /// <summary>"Netherite", "netherite" or "minecraft:netherite" to "minecraft:netherite"; null when unknown or blank.</summary>
    public static string? NormalizeMaterial(string? key) => Normalize(key, KnownMaterials);

    private static string? Normalize(string? key, HashSet<string> known)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var trimmed = key.Trim().ToLowerInvariant();
        if (!trimmed.Contains(':')) trimmed = $"{Namespace}:{trimmed}";
        return known.Contains(trimmed) ? trimmed : null;
    }
}
