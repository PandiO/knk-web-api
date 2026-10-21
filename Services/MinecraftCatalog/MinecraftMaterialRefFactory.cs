using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>
/// The one way to make a <see cref="MinecraftMaterialRef"/> outside a server scan (KNG-200 §3): the key
/// normalised, a display name, and <see cref="MinecraftCatalogStatus.Unverified"/> until a scan sees the
/// key. Seeds, the get-or-create endpoint and the inline creation in ItemBlueprint/Category use it.
/// </summary>
public static class MinecraftMaterialRefFactory
{
    /// <param name="namespaceKey">Any notation; normalised here.</param>
    /// <param name="staticEntry">The static catalogue's entry for the key, if any (until phase 5 removes it).</param>
    /// <param name="fallbackCategory">Legacy category when the static catalogue doesn't know the key.</param>
    /// <exception cref="ArgumentException">The key is not a valid namespaced key.</exception>
    public static MinecraftMaterialRef NewUnverified(string namespaceKey, MinecraftMaterialCatalogEntry? staticEntry, string fallbackCategory)
    {
        var key = MinecraftNamespaceKey.Normalize(namespaceKey);
        return new MinecraftMaterialRef
        {
            NamespaceKey = key,
            Category = staticEntry?.Category ?? fallbackCategory,
            LegacyName = staticEntry?.LegacyName,
            IconUrl = staticEntry?.IconUrl,
            DisplayName = string.IsNullOrWhiteSpace(staticEntry?.DisplayName)
                ? MinecraftNamespaceKey.DisplayNameFromKey(key)
                : staticEntry!.DisplayName,
            CatalogStatus = MinecraftCatalogStatus.Unverified,
        };
    }
}
