namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>
/// Translates picker filters to database filters (KNG-200): the legacy <c>categoryFilter</c> values of
/// the old static catalogue become a kind, and Missing rows are hidden.
/// </summary>
public static class MinecraftPickerFilters
{
    public const string Kind = "kind";
    public const string KeyContains = "keyContains";
    public const string ExcludeMissing = "excludeMissing";

    /// <summary>Search filter that makes a search a picker search (its pre-KNG-200 name <c>SearchHybrid</c> went with KNG-250).</summary>
    public const string Picker = "Picker";

    /// <summary>Enchantment picker filter (KNG-250): <c>vanilla</c>, <c>custom</c>, or absent for both.</summary>
    public const string Origin = "origin";

    /// <summary>True when the query's filters ask for a picker search.</summary>
    public static bool IsPickerSearch(IReadOnlyDictionary<string, string>? filters) =>
        filters != null && filters.Any(f =>
            string.Equals(f.Key, Picker, StringComparison.OrdinalIgnoreCase)
            && string.Equals(f.Value, "true", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Database filters for a picker query: <c>kind</c> as sent (item/block), else from <c>category</c>
    /// (ITEM/ICON → item, BLOCK → block, BANNER → keys containing <c>_banner</c>, ANY → no filter).
    /// </summary>
    public static Dictionary<string, string> ForDatabase(IReadOnlyDictionary<string, string>? pickerFilters)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ExcludeMissing] = "true" };
        string? Get(string name) => pickerFilters?.FirstOrDefault(f => string.Equals(f.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

        var kind = Get(Kind)?.Trim().ToLowerInvariant();
        var category = Get("category")?.Trim().ToUpperInvariant();
        switch (category)
        {
            case "ITEM" or "ICON":
                kind = "item";
                break;
            case "BLOCK":
                kind = "block";
                break;
            case "BANNER":
                result[KeyContains] = "_banner";
                break;
            case "ANY":
                kind = null;
                break;
        }
        if (kind is "item" or "block") result[Kind] = kind;
        return result;
    }

    /// <summary>
    /// Database filters for an enchantment picker query (KNG-250 §12.5): <c>origin</c> (vanilla/custom) and the
    /// legacy <c>category</c> as sent, Missing rows hidden.
    /// </summary>
    public static Dictionary<string, string> ForEnchantmentDatabase(IReadOnlyDictionary<string, string>? pickerFilters)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ExcludeMissing] = "true" };
        string? Get(string name) => pickerFilters?.FirstOrDefault(f => string.Equals(f.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

        var origin = Get(Origin)?.Trim().ToLowerInvariant();
        if (origin is "vanilla" or "custom") result[Origin] = origin;
        var category = Get("category")?.Trim();
        if (!string.IsNullOrEmpty(category)) result["category"] = category;
        return result;
    }
}
