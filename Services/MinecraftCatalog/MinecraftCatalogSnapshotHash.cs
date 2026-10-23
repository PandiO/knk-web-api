using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>
/// The snapshot fingerprint both knk-plugin and the API compute (KNG-200 plan §5.2a): entries sorted by
/// key, one line <c>key|isItem|isBlock|maxStackSize|translationKey|displayName|defaultBlockState</c>
/// each (null → empty), joined with "\n", SHA-256 over UTF-8 as lower-case hex. Schema 2 (KNG-250 §12.2)
/// appends <c>"\n#enchantments"</c> and one <c>"\n" + key|isCustom|maxLevel|translationKey|displayName|category</c>
/// per enchantment sorted by key.
/// </summary>
public static class MinecraftCatalogSnapshotHash
{
    /// <param name="enchantments">The schema 2 enchantments; null for a schema 1 snapshot.</param>
    public static string Compute(IEnumerable<MinecraftCatalogEntryDto> entries, IEnumerable<MinecraftCatalogEnchantmentDto>? enchantments = null)
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
        var text = new StringBuilder(string.Join('\n', lines));
        if (enchantments != null)
        {
            text.Append("\n#enchantments");
            foreach (var e in enchantments.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                text.Append('\n').Append(string.Join('|',
                    e.Key,
                    e.IsCustom ? "true" : "false",
                    e.MaxLevel?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    e.TranslationKey ?? string.Empty,
                    e.DisplayName ?? string.Empty,
                    e.Category ?? string.Empty));
            }
        }
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
