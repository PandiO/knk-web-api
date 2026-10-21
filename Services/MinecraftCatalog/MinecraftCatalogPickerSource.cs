using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;

namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>Where material and block pickers search (KNG-200 phase 4, plan §9.3).</summary>
public enum MinecraftPickerSource
{
    /// <summary>The database once a scan has completed, before that the old hybrid merge.</summary>
    Auto = 0,
    /// <summary>Always the static catalogue merged with the database (rollback).</summary>
    Hybrid = 1,
    /// <summary>Always the database only.</summary>
    Database = 2,
}

/// <summary>Config section <c>MinecraftCatalog</c>.</summary>
public sealed class MinecraftCatalogOptions
{
    public const string SectionName = "MinecraftCatalog";

    public MinecraftPickerSource PickerSource { get; set; } = MinecraftPickerSource.Auto;
}

public interface IMinecraftCatalogPickerSource
{
    /// <summary>True when pickers search the database only, not the static catalogue.</summary>
    Task<bool> UseDatabaseAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="IMinecraftCatalogPickerSource"/>
public sealed class MinecraftCatalogPickerSource : IMinecraftCatalogPickerSource
{
    private readonly KnKDbContext _db;
    private readonly MinecraftCatalogOptions _options;

    public MinecraftCatalogPickerSource(KnKDbContext db, MinecraftCatalogOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<bool> UseDatabaseAsync(CancellationToken ct = default) => _options.PickerSource switch
    {
        MinecraftPickerSource.Database => true,
        MinecraftPickerSource.Hybrid => false,
        _ => await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
            _db.MinecraftCatalogScans, s => s.Status == MinecraftCatalogScanStatus.Completed, ct),
    };
}

/// <summary>
/// Translates picker filters to database filters (KNG-200 phase 4): the legacy <c>categoryFilter</c>
/// values of the static catalogue become a kind, and Missing rows are hidden.
/// </summary>
public static class MinecraftPickerFilters
{
    public const string Kind = "kind";
    public const string KeyContains = "keyContains";
    public const string ExcludeMissing = "excludeMissing";

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
