using System.Text.Json;

namespace knkwebapi_v2.Services.Map;

/// <summary>
/// The stored form of an outline's points (<see cref="Models.MapRegionOutline.PointsJson"/>): a JSON array of
/// <c>[x, z]</c> pairs, for example <c>[[0,0],[10,0],[10,10],[0,10]]</c>. Readers (the public map endpoints, #736) use
/// <see cref="Parse"/>.
/// </summary>
public static class MapOutlinePoints
{
    public static string Serialize(IEnumerable<(int X, int Z)> points) =>
        JsonSerializer.Serialize(points.Select(p => new[] { p.X, p.Z }));

    /// <summary>The points of a stored outline; an empty list for a value that is not a list of pairs.</summary>
    public static IReadOnlyList<(int X, int Z)> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<(int, int)>();
        try
        {
            var pairs = JsonSerializer.Deserialize<int[][]>(json);
            if (pairs == null || pairs.Any(p => p == null || p.Length != 2)) return Array.Empty<(int, int)>();
            return pairs.Select(p => (p[0], p[1])).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<(int, int)>();
        }
    }
}
