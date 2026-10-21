using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>
/// The snapshot fingerprint both knk-plugin and the API compute (KNG-200 plan §5.2a): entries sorted by
/// key, one line <c>key|isItem|isBlock|maxStackSize|translationKey|displayName|defaultBlockState</c>
/// each (null → empty), joined with "\n", SHA-256 over UTF-8 as lower-case hex.
/// </summary>
public static class MinecraftCatalogSnapshotHash
{
    public static string Compute(IEnumerable<MinecraftCatalogEntryDto> entries)
    {
        var lines = entries
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => string.Join('|',
                e.Key,
                e.IsItem ? "true" : "false",
                e.IsBlock ? "true" : "false",
                e.MaxStackSize?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                e.TranslationKey ?? string.Empty,
                e.DisplayName ?? string.Empty,
                e.DefaultBlockState ?? string.Empty));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
