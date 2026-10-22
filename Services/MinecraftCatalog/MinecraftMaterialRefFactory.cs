using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>
/// The one way to make a <see cref="MinecraftMaterialRef"/> outside a server scan (KNG-200 §3): the key
/// normalised, a display name, and <see cref="MinecraftCatalogStatus.Unverified"/> until a scan sees the
/// key. Seeds, the get-or-create endpoints and the inline creation in ItemBlueprint/Category use it; the
/// server scan fills in names, flags and icons (KNG-200 phase 5 removed the static catalogue).
/// </summary>
public static class MinecraftMaterialRefFactory
{
    /// <param name="namespaceKey">Any notation; normalised here.</param>
    /// <param name="category">Legacy category for the new row (the server scan sets the real item/block flags).</param>
    /// <exception cref="ArgumentException">The key is not a valid namespaced key.</exception>
    public static MinecraftMaterialRef NewUnverified(string namespaceKey, string category)
    {
        var key = MinecraftNamespaceKey.Normalize(namespaceKey);
        return new MinecraftMaterialRef
        {
            NamespaceKey = key,
            Category = category,
            DisplayName = MinecraftNamespaceKey.DisplayNameFromKey(key),
            CatalogStatus = MinecraftCatalogStatus.Unverified,
        };
    }
}

/// <summary>A material with this normalised key already exists (HTTP 409 with its id, KNG-200 phase 1).</summary>
public class MinecraftMaterialRefConflictException : InvalidOperationException
{
    public MinecraftMaterialRefConflictException(string namespaceKey, int existingId)
        : base($"A MinecraftMaterialRef with key {namespaceKey} already exists (id {existingId}).")
    {
        NamespaceKey = namespaceKey;
        ExistingId = existingId;
    }

    public string NamespaceKey { get; }

    public int ExistingId { get; }
}

/// <summary>A block ref with this canonical state already exists (HTTP 409 with its id, KNG-200 phase 1).</summary>
public class MinecraftBlockRefConflictException : InvalidOperationException
{
    public MinecraftBlockRefConflictException(string state, int existingId)
        : base($"A MinecraftBlockRef for {state} already exists (id {existingId}).")
    {
        ExistingId = existingId;
    }

    public int ExistingId { get; }
}
