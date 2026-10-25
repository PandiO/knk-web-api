using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>
/// Canonical block states and their identity (KNG-200 §4). The canonical form is
/// <c>namespace:key[prop=value,...]</c>: lower case, properties sorted by name, no spaces. A state
/// without properties is no state (null): "the block, any state". The plugin canonicalises the same way
/// after <c>Bukkit.createBlockData(s).getAsString()</c>, so a state typed in the web app and one
/// captured in game get the same <see cref="StateKey"/>.
/// </summary>
public static class MinecraftBlockState
{
    /// <summary>Result of <see cref="TryCanonicalize"/>.</summary>
    public sealed record Canonical(string NamespaceKey, string? State);

    /// <summary>
    /// Canonicalises <paramref name="state"/> for the block <paramref name="namespaceKey"/>. The state may be
    /// a full string (<c>minecraft:oak_stairs[facing=north]</c>), properties only (<c>[facing=north]</c> or
    /// <c>facing=north</c>) or blank. Returns null when the key is invalid, the state names a different
    /// block, or a property is malformed or given twice.
    /// </summary>
    public static Canonical? TryCanonicalize(string? namespaceKey, string? state)
    {
        var key = MinecraftNamespaceKey.TryNormalize(namespaceKey);
        if (key == null) return null;
        if (string.IsNullOrWhiteSpace(state)) return new Canonical(key, null);

        var text = state.Trim();
        string properties;
        var open = text.IndexOf('[');
        if (open >= 0)
        {
            if (!text.EndsWith(']')) return null;
            var prefix = text[..open];
            if (prefix.Length > 0 && MinecraftNamespaceKey.TryNormalize(prefix) != key) return null;
            properties = text[(open + 1)..^1];
        }
        else if (text.Contains('='))
        {
            properties = text;
        }
        else
        {
            // Just a key: the block in any state.
            return MinecraftNamespaceKey.TryNormalize(text) == key ? new Canonical(key, null) : null;
        }

        var pairs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in properties.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) return null;
            var name = part[..eq].Trim().ToLower(CultureInfo.InvariantCulture);
            var value = part[(eq + 1)..].Trim().ToLower(CultureInfo.InvariantCulture);
            if (name.Length == 0 || value.Length == 0 || name.Any(char.IsWhiteSpace) || value.Any(char.IsWhiteSpace)) return null;
            if (!pairs.TryAdd(name, value)) return null;
        }

        if (pairs.Count == 0) return new Canonical(key, null);
        return new Canonical(key, key + "[" + string.Join(',', pairs.Select(p => p.Key + "=" + p.Value)) + "]");
    }

    /// <summary>SHA-256 (lower-case hex) of the normalised key and canonical state: the identity of a block ref.</summary>
    public static string StateKey(Canonical canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.NamespaceKey + "|" + (canonical.State ?? string.Empty)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
