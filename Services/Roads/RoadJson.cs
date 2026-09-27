using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Json;

namespace knkwebapi_v2.Services.Roads;

/// <summary>
/// The road entities' JSON columns and the DTO shapes they map to (geometry, id lists, flag names,
/// opaque statistics). Everything goes through <see cref="JsonColumn"/> (plan R29).
/// </summary>
public static class RoadJson
{
    public static int[][] Geometry(string? json) =>
        JsonColumn.Deserialize<int[][]>(json) ?? Array.Empty<int[]>();

    public static string GeometryJson(int[][] geometry) => JsonColumn.Serialize(geometry);

    public static List<int> IntList(string? json) => JsonColumn.DeserializeList<int>(json);

    public static List<string> StringList(string? json) => JsonColumn.DeserializeList<string>(json);

    public static string ListJson<T>(IEnumerable<T>? values) => JsonColumn.Serialize(values?.ToList() ?? new List<T>());

    /// <summary>Null or unparsable → null (the DTO omits it); the column keeps whatever it held.</summary>
    public static JsonElement? Element(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
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

    /// <summary>Raw JSON of an element; "{}" for null/undefined.</summary>
    public static string ElementJson(JsonElement? element) =>
        element is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } e ? e.GetRawText() : "{}";

    public static List<string> FlagNames(RoadEdgeFlags flags) =>
        Enum.GetValues<RoadEdgeFlags>()
            .Where(f => f != RoadEdgeFlags.None && flags.HasFlag(f))
            .Select(f => f.ToString())
            .ToList();

    /// <summary>Case-insensitive names → set; unknown names are an ArgumentException.</summary>
    public static RoadEdgeFlags ParseFlags(IEnumerable<string>? names)
    {
        var flags = RoadEdgeFlags.None;
        foreach (var name in names ?? Enumerable.Empty<string>())
        {
            if (!Enum.TryParse<RoadEdgeFlags>(name, ignoreCase: true, out var flag) || flag == RoadEdgeFlags.None)
            {
                throw new ArgumentException($"Unknown edge flag '{name}' (expected Oneway, NoGps or Closed).");
            }
            flags |= flag;
        }
        return flags;
    }
}
