namespace knkwebapi_v2.Models;

// Banner pattern keys accepted for BannerLayer.PatternKey - mirrors the Bukkit/Minecraft
// banner_pattern registry of the server version knk-plugin compiles against (paper-api 1.21.10).
// When the server is upgraded and Mojang adds patterns, add their keys here; knk-plugin's
// BannerDesignBukkitMapper skips (and logs) any key its running server doesn't know.
public static class BannerPatternKeys
{
    public const string Namespace = "minecraft";

    public static readonly IReadOnlyList<string> All = new[]
    {
        "minecraft:base",
        "minecraft:square_bottom_left",
        "minecraft:square_bottom_right",
        "minecraft:square_top_left",
        "minecraft:square_top_right",
        "minecraft:stripe_bottom",
        "minecraft:stripe_top",
        "minecraft:stripe_left",
        "minecraft:stripe_right",
        "minecraft:stripe_center",
        "minecraft:stripe_middle",
        "minecraft:stripe_downright",
        "minecraft:stripe_downleft",
        "minecraft:small_stripes",
        "minecraft:cross",
        "minecraft:straight_cross",
        "minecraft:triangle_bottom",
        "minecraft:triangle_top",
        "minecraft:triangles_bottom",
        "minecraft:triangles_top",
        "minecraft:diagonal_left",
        "minecraft:diagonal_up_right",
        "minecraft:diagonal_up_left",
        "minecraft:diagonal_right",
        "minecraft:circle",
        "minecraft:rhombus",
        "minecraft:half_vertical",
        "minecraft:half_horizontal",
        "minecraft:half_vertical_right",
        "minecraft:half_horizontal_bottom",
        "minecraft:border",
        "minecraft:curly_border",
        "minecraft:gradient",
        "minecraft:gradient_up",
        "minecraft:bricks",
        "minecraft:globe",
        "minecraft:creeper",
        "minecraft:skull",
        "minecraft:flower",
        "minecraft:mojang",
        "minecraft:piglin",
        "minecraft:flow",
        "minecraft:guster",
    };

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    /// <summary>
    /// Normalizes an authored key ("Stripe_Top", "stripe_top", "minecraft:stripe_top") to its
    /// canonical namespaced lowercase form, or returns null when it isn't a known pattern.
    /// </summary>
    public static string? Normalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var trimmed = key.Trim().ToLowerInvariant();
        if (!trimmed.Contains(':')) trimmed = $"{Namespace}:{trimmed}";
        return Known.Contains(trimmed) ? trimmed : null;
    }
}
