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

    /// <summary>Search filter that makes a search a picker search; <c>SearchHybrid</c> is its pre-KNG-200 name.</summary>
    public const string Picker = "Picker";
    public const string LegacyPicker = "SearchHybrid";

    /// <summary>True when the query's filters ask for a picker search.</summary>
    public static bool IsPickerSearch(IReadOnlyDictionary<string, string>? filters) =>
        filters != null && filters.Any(f =>
            (string.Equals(f.Key, Picker, StringComparison.OrdinalIgnoreCase) || string.Equals(f.Key, LegacyPicker, StringComparison.OrdinalIgnoreCase))
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
}
