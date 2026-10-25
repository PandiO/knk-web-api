using System.Globalization;
using System.Text.RegularExpressions;

namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>
/// The one normalisation of a Minecraft namespaced key (KNG-200 §3): trimmed, lower case, no inner
/// whitespace, <c>minecraft:</c> added when there is no namespace, and the characters Minecraft allows.
/// The web app and the plugin apply the same rules.
/// </summary>
public static class MinecraftNamespaceKey
{
    public const string DefaultNamespace = "minecraft";

    private static readonly Regex Valid = new("^[a-z0-9_.-]+:[a-z0-9_./-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The normalised key, or null when <paramref name="raw"/> can't be one (blank, inner whitespace, bad characters).</summary>
    public static string? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var key = raw.Trim().ToLower(CultureInfo.InvariantCulture);
        if (!key.Contains(':')) key = DefaultNamespace + ":" + key;
        return Valid.IsMatch(key) ? key : null;
    }

    /// <exception cref="ArgumentException">When <paramref name="raw"/> is not a valid namespaced key.</exception>
    public static string Normalize(string? raw) =>
        TryNormalize(raw) ?? throw new ArgumentException($"\"{raw}\" is not a valid Minecraft namespaced key (e.g. minecraft:oak_planks).", nameof(raw));

    /// <summary>A readable name derived from the key: <c>minecraft:oak_stairs</c> → "Oak Stairs" (KNG-200 D4 fallback).</summary>
    public static string DisplayNameFromKey(string key)
    {
        var path = key.Contains(':') ? key[(key.IndexOf(':') + 1)..] : key;
        var last = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
        var words = last.Split(new[] { '_', '.', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        return string.Join(' ', words);
    }
}
