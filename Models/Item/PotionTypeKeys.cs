namespace knkwebapi_v2.Models;

/// <summary>
/// Potion type keys accepted for <see cref="ItemBlueprint.PotionType"/> (siege shop, PandiO/knk-workspace#867, DESIGN
/// § 9.4) - mirrors the vanilla <c>potion</c> registry of the server version knk-plugin compiles against (paper-api
/// 26.3), the way <see cref="ArmorTrimKeys"/> mirrors the trims. "strong_" is level II, "long_" the extended
/// duration. When Mojang adds a potion, add its key here. A key in another namespace (a datapack) is accepted as typed.
/// </summary>
public static class PotionTypeKeys
{
    public const string Namespace = "minecraft";

    public static readonly IReadOnlyList<string> All = new[]
    {
        "water", "mundane", "thick", "awkward",
        "night_vision", "long_night_vision", "invisibility", "long_invisibility",
        "leaping", "long_leaping", "strong_leaping", "fire_resistance", "long_fire_resistance",
        "swiftness", "long_swiftness", "strong_swiftness", "slowness", "long_slowness", "strong_slowness",
        "turtle_master", "long_turtle_master", "strong_turtle_master", "water_breathing", "long_water_breathing",
        "healing", "strong_healing", "harming", "strong_harming", "poison", "long_poison", "strong_poison",
        "regeneration", "long_regeneration", "strong_regeneration", "strength", "long_strength", "strong_strength",
        "weakness", "long_weakness", "luck", "slow_falling", "long_slow_falling",
        "wind_charged", "weaving", "oozing", "infested",
    }.Select(k => $"{Namespace}:{k}").ToArray();

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    /// <summary>
    /// Null/blank → null. Otherwise lower-cased, "minecraft:" added when there is no namespace, and checked: a
    /// minecraft key must be a vanilla potion, any key must look like "namespace:path". Throws ArgumentException (400).
    /// </summary>
    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        var key = trimmed.ToLowerInvariant().Replace(' ', '_');
        if (!key.Contains(':')) key = $"{Namespace}:{key}";
        if (key.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9_.-]+:[a-z0-9_./-]+$"))
            throw new ArgumentException($"'{value}' is not a potion type key (e.g. strong_healing or minecraft:regeneration).");
        if (key.StartsWith(Namespace + ":", StringComparison.Ordinal) && !Known.Contains(key))
            throw new ArgumentException($"'{value}' is not a vanilla potion type (e.g. healing, strong_healing, regeneration).");
        return key;
    }
}
