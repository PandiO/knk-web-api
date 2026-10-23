using System.Globalization;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>
/// Enchantment keys (KNG-250, docs/specs/minecraft-catalog/IMPLEMENTATION_PLAN.md §12.3). Vanilla keys are
/// normalised like material keys; custom ones live in the <c>knk</c> namespace, so a bare custom key must
/// not get <c>minecraft:</c>.
/// </summary>
public static class MinecraftEnchantmentKey
{
    public const string CustomNamespace = "knk";

    /// <summary>The normalised key: a bare key gets <c>knk:</c> when custom, else <c>minecraft:</c>; null when invalid.</summary>
    public static string? TryNormalize(string? raw, bool isCustom)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var key = raw.Trim();
        if (!key.Contains(':') && isCustom) key = CustomNamespace + ":" + key;
        return MinecraftNamespaceKey.TryNormalize(key);
    }

    /// <exception cref="ArgumentException">When <paramref name="raw"/> is not a valid namespaced key.</exception>
    public static string Normalize(string? raw, bool isCustom) =>
        TryNormalize(raw, isCustom) ?? throw new ArgumentException($"\"{raw}\" is not a valid enchantment key (e.g. minecraft:sharpness or knk:chaos).", nameof(raw));

    /// <summary>True when a key without namespace or with any namespace other than <c>minecraft</c> is meant.</summary>
    public static bool IsCustomKey(string normalizedKey) =>
        !normalizedKey.StartsWith(MinecraftNamespaceKey.DefaultNamespace + ":", StringComparison.Ordinal);

    /// <summary>
    /// The scanned key an existing ref or definition belongs to (§12.3), or null when its key is invalid.
    /// </summary>
    /// <param name="rawKey">The stored key.</param>
    /// <param name="isCustom">The stored row's custom flag.</param>
    /// <param name="scan">The scanned keys.</param>
    public static string? Resolve(string? rawKey, bool isCustom, ScannedEnchantmentKeys scan)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return null;
        var raw = rawKey.Trim();
        var colon = raw.IndexOf(':');
        var ns = colon >= 0 ? raw[..colon].Trim().ToLower(CultureInfo.InvariantCulture) : null;

        if (ns == MinecraftNamespaceKey.DefaultNamespace) return MinecraftNamespaceKey.TryNormalize(raw);

        if ((ns is null or CustomNamespace) && scan.TryGetCustom(AbilityDefinition.MatchKey(raw), out var customKey))
        {
            // A bare key is only custom when the row says so or no vanilla enchantment has that name.
            var vanillaKey = ns == null ? MinecraftNamespaceKey.TryNormalize(raw) : null;
            if (ns == CustomNamespace || isCustom || vanillaKey == null || !scan.Contains(vanillaKey)) return customKey;
        }

        return ns == null ? TryNormalize(raw, isCustom) : MinecraftNamespaceKey.TryNormalize(raw);
    }
}

/// <summary>The keys of one snapshot's enchantments, with the custom ones by <see cref="AbilityDefinition.MatchKey"/>.</summary>
public sealed class ScannedEnchantmentKeys
{
    private readonly HashSet<string> _keys;
    private readonly Dictionary<string, string> _customByMatchKey = new(StringComparer.Ordinal);

    public ScannedEnchantmentKeys(IEnumerable<(string Key, bool IsCustom)> enchantments)
    {
        var list = enchantments.ToList();
        _keys = list.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        // The first key wins when two custom keys only differ in punctuation (knk:flash_chaos, knk:flashchaos).
        foreach (var (key, _) in list.Where(e => e.IsCustom).OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            _customByMatchKey.TryAdd(AbilityDefinition.MatchKey(key), key);
        }
    }

    public bool Contains(string key) => _keys.Contains(key);

    public bool TryGetCustom(string matchKey, out string key) => _customByMatchKey.TryGetValue(matchKey, out key!);
}
