using System.Text.Json;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.LocationRetention;

/// <summary>
/// References to a Location that are not foreign keys, so the EF model can't see them (KNG-80):
/// Location ids stored inside JSON. A Location any source names is never flagged, and a delete
/// re-checks every source. Each source is a scoped service; add one for any new JSON or config
/// place that stores a Location id.
/// </summary>
public interface ILocationReferenceSource
{
    /// <summary>Referenced Location id → where (e.g. "Game settings: join spawn").</summary>
    Task<IReadOnlyDictionary<int, string>> FindReferencedLocationIdsAsync(CancellationToken ct = default);
}

/// <summary>
/// Game settings (KNG-52): the join spawn, the default respawn policy and every per-world spawn and
/// respawn are LocationReferenceDto JSON. A reference with sourceType "Location" names its Location in
/// sourceId; any reference may also carry a snapshot with a locationId.
/// </summary>
public sealed class GameSettingsLocationReferenceSource : ILocationReferenceSource
{
    private readonly KnKDbContext _context;

    public GameSettingsLocationReferenceSource(KnKDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyDictionary<int, string>> FindReferencedLocationIdsAsync(CancellationToken ct = default)
    {
        var found = new Dictionary<int, string>();
        var rows = await _context.GameSettings.AsNoTracking()
            .Select(g => new { g.JoinSpawnReferenceJson, g.DefaultRespawnPolicyJson, g.WorldSettingsJson })
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            LocationJson.CollectReferences(row.JoinSpawnReferenceJson, "Game settings: join spawn", found);
            LocationJson.CollectReferences(row.DefaultRespawnPolicyJson, "Game settings: default respawn", found);
            LocationJson.CollectReferences(row.WorldSettingsJson, "Game settings: per-world spawn/respawn", found);
        }
        return found;
    }
}

/// <summary>
/// Unfinished FormWizard drafts (InProgress or Paused FormSubmissionProgress): a draft may already
/// point at a Location it created or picked ("LocationId": 12, or a Location object with its id),
/// and resuming it must still find that Location. Older drafts than the grace period are exactly
/// the ones the grace period no longer covers.
/// </summary>
public sealed class FormDraftLocationReferenceSource : ILocationReferenceSource
{
    private static readonly string[] OpenStatuses = { "InProgress", "Paused" };
    private readonly KnKDbContext _context;

    public FormDraftLocationReferenceSource(KnKDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyDictionary<int, string>> FindReferencedLocationIdsAsync(CancellationToken ct = default)
    {
        var found = new Dictionary<int, string>();
        var drafts = await _context.FormSubmissionProgresses.AsNoTracking()
            .Where(p => OpenStatuses.Contains(p.Status))
            .Select(p => new { p.Id, p.CurrentStepDataJson, p.AllStepsDataJson })
            .ToListAsync(ct);
        foreach (var draft in drafts)
        {
            var where = $"Unfinished form draft #{draft.Id}";
            LocationJson.CollectFormValues(draft.CurrentStepDataJson, where, found);
            LocationJson.CollectFormValues(draft.AllStepsDataJson, where, found);
        }
        return found;
    }
}

/// <summary>Walks JSON for Location ids. Malformed JSON is skipped, never thrown.</summary>
public static class LocationJson
{
    /// <summary>LocationReferenceDto shapes: {sourceType:"Location", sourceId:N} and {locationId:N} anywhere.</summary>
    public static void CollectReferences(string? json, string where, IDictionary<int, string> into)
    {
        var root = Parse(json);
        if (root.HasValue) WalkReferences(root.Value, where, into);
    }

    /// <summary>Form step data: any property whose name contains "location" holding an id, an object
    /// with an id, or a list of those (e.g. "LocationId": 12, "HubLocation": {"id": 12}).</summary>
    public static void CollectFormValues(string? json, string where, IDictionary<int, string> into)
    {
        var root = Parse(json);
        if (root.HasValue) WalkFormValues(root.Value, locationContext: false, where, into);
    }

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WalkReferences(JsonElement element, string where, IDictionary<int, string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                string? sourceType = null;
                int? sourceId = null;
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, "sourceType", StringComparison.OrdinalIgnoreCase))
                    {
                        sourceType = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    }
                    else if (string.Equals(property.Name, "sourceId", StringComparison.OrdinalIgnoreCase))
                    {
                        sourceId = AsId(property.Value);
                    }
                    else if (string.Equals(property.Name, "locationId", StringComparison.OrdinalIgnoreCase) && AsId(property.Value) is { } locationId)
                    {
                        into.TryAdd(locationId, where);
                    }
                    WalkReferences(property.Value, where, into);
                }
                if (sourceId.HasValue && string.Equals(sourceType, "Location", StringComparison.OrdinalIgnoreCase))
                {
                    into.TryAdd(sourceId.Value, where);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) WalkReferences(item, where, into);
                break;
        }
    }

    private static void WalkFormValues(JsonElement element, bool locationContext, string where, IDictionary<int, string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var isLocationProperty = property.Name.Contains("location", StringComparison.OrdinalIgnoreCase);
                    if (locationContext && string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase) && AsId(property.Value) is { } objectId)
                    {
                        into.TryAdd(objectId, where);
                    }
                    else if (isLocationProperty && AsId(property.Value) is { } id)
                    {
                        into.TryAdd(id, where);
                    }
                    WalkFormValues(property.Value, isLocationProperty, where, into);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) WalkFormValues(item, locationContext, where, into);
                break;
        }
    }

    private static int? AsId(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt32(out var n) && n > 0 => n,
        JsonValueKind.String when int.TryParse(value.GetString(), out var s) && s > 0 => s,
        _ => null
    };
}
