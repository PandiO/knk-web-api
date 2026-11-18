using System.Reflection;
using System.Text.Json.Serialization;
using knkwebapi_v2.Dtos;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Overview map L2 (PandiO/knk-workspace#736, A05, O02, O16, O32): the public map DTOs are an allow-list. These tests pin
/// the exact JSON property set of every type served anonymously, so a new field fails here until it is reviewed against
/// O32 and added on purpose. <c>MapPublicServiceTests</c> checks the same sets on real serialized responses.
/// </summary>
public sealed class MapPublicDtoFieldTests
{
    public static readonly IReadOnlyDictionary<Type, string[]> Pinned = new Dictionary<Type, string[]>
    {
        // Panels (O32).
        [typeof(MapTownPanelDto)] = new[] { "id", "name", "description", "districtCount" },
        [typeof(MapDistrictPanelDto)] = new[] { "id", "name", "description", "townName" },
        [typeof(MapStructurePanelDto)] = new[]
        {
            "id", "name", "description", "type", "subtype", "districtName", "townName", "streetName", "houseNumber", "gate",
        },
        [typeof(MapGatePanelDto)] = new[] { "underSiege", "doors" },
        [typeof(MapGateDoorPanelDto)] = new[] { "name", "openState", "isDestroyed", "healthCurrent", "healthMax" },
        // Markers (spec § 6.2.2, O18, O20).
        [typeof(MapMarkersDto)] = new[] { "version", "towns", "districts", "structures" },
        [typeof(MapTownMarkerDto)] = new[] { "id", "name", "pin", "outlines" },
        [typeof(MapDistrictMarkerDto)] = new[] { "id", "townId", "name", "pin", "outlines" },
        [typeof(MapStructureMarkerDto)] = new[]
        {
            "id", "districtId", "townId", "name", "type", "subtype", "typeLabel", "pin", "outlines",
        },
        [typeof(MapPinDto)] = new[] { "world", "x", "y", "z", "source" },
        [typeof(MapOutlineDto)] = new[] { "world", "points", "minY", "maxY" },
        // Configuration (spec § 6.2.1, O19).
        [typeof(MapConfigDto)] = new[] { "enabled", "markerVersion", "labels", "flash", "livePlayers", "gateCommands" },
        [typeof(MapLabelDistancesDto)] = new[] { "town", "district", "structure", "arena" },
        [typeof(MapFlashDto)] = new[] { "count", "durationMs" },
        [typeof(MapPartConfigDto)] = new[] { "enabled" },
        [typeof(MapLivePlayersConfigDto)] = new[] { "enabled", "pollIntervalMs" },
    };

    public static string[] JsonNames(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? $"<no JsonPropertyName: {p.Name}>")
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();

    public static IEnumerable<object[]> Types() => Pinned.Keys.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(Types))]
    public void PublicDto_HasExactlyThePinnedFields(Type type)
    {
        Assert.Equal(Pinned[type].OrderBy(n => n, StringComparer.Ordinal).ToArray(), JsonNames(type));
    }

    [Fact]
    public void EveryPublicMapDto_IsPinned()
    {
        var dtos = typeof(MapTownPanelDto).Assembly.GetTypes()
            .Where(t => t.IsClass && t.Namespace == typeof(MapTownPanelDto).Namespace && t.Name.StartsWith("Map") && t.Name.EndsWith("Dto"))
            .ToList();
        Assert.Empty(dtos.Where(t => !Pinned.ContainsKey(t)).Select(t => t.Name));
    }

    [Fact]
    public void NoPublicMapDto_NamesARegionOwnerOrPermission()
    {
        var forbidden = new[] { "region", "wg", "owner", "user", "uuid", "permission", "location", "street" + "Id", "siege" + "Id" };
        foreach (var type in Pinned.Keys)
        {
            foreach (var name in JsonNames(type))
            {
                Assert.DoesNotContain(forbidden, f => name.Contains(f, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
