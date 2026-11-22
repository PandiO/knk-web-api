using knkwebapi_v2.Services.Regions;

namespace knkwebapi_v2.Tests.Services.Regions;

/// <summary>
/// PandiO/knk-workspace#988: a game server stand-in for the spot size and site rules. Regions not added answer
/// "not found"; with <see cref="Offline"/> every query is unavailable, as with no game server connected.
/// </summary>
public sealed class FakeGameRegionQueryClient : IGameRegionQueryClient
{
    private readonly Dictionary<string, RegionGeometry> _regions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpotSiteCheckAnswer> _sites = new(StringComparer.OrdinalIgnoreCase);

    public bool Offline { get; set; }

    /// <summary>The (region, world) pairs asked, in order.</summary>
    public List<(string Region, string? World)> Asked { get; } = new();

    public FakeGameRegionQueryClient Box(string region, int minX, int minY, int minZ, int maxX, int maxY, int maxZ,
        string shape = "cuboid", string? world = "world")
    {
        _regions[region] = new RegionGeometry(world, shape, minX, minY, minZ, maxX, maxY, maxZ, Array.Empty<(int, int)>());
        return this;
    }

    public FakeGameRegionQueryClient Site(string region, int openFloor = 0, long blocked = 0, bool volumeChecked = true,
        params string[] warnings)
    {
        _sites[region] = new SpotSiteCheckAnswer(RegionQueryStatus.Found, "Checked.", 63, openFloor, volumeChecked, blocked,
            warnings);
        return this;
    }

    public Task<RegionGeometryAnswer> GetGeometryAsync(string regionId, string? world, CancellationToken ct = default)
    {
        Asked.Add((regionId, world));
        if (Offline)
            return Task.FromResult(new RegionGeometryAnswer(RegionQueryStatus.Unavailable,
                "No game server is connected, so the region's size wasn't checked.", null));
        return Task.FromResult(_regions.TryGetValue(regionId, out var g)
            ? new RegionGeometryAnswer(RegionQueryStatus.Found, "Region found.", g)
            : new RegionGeometryAnswer(RegionQueryStatus.NotFound, $"Region '{regionId}' doesn't exist on the game server.", null));
    }

    public Task<SpotSiteCheckAnswer> CheckSpotSiteAsync(string regionId, string? world, CancellationToken ct = default)
    {
        Asked.Add((regionId, world));
        if (Offline || !_regions.ContainsKey(regionId) && !_sites.ContainsKey(regionId))
            return Task.FromResult(Offline
                ? new SpotSiteCheckAnswer(RegionQueryStatus.Unavailable, "No game server is connected, so the spot's floor and space wasn't checked.", 0, 0, false, 0, Array.Empty<string>())
                : new SpotSiteCheckAnswer(RegionQueryStatus.NotFound, $"Region '{regionId}' doesn't exist on the game server.", 0, 0, false, 0, Array.Empty<string>()));
        return Task.FromResult(_sites.TryGetValue(regionId, out var s)
            ? s
            : new SpotSiteCheckAnswer(RegionQueryStatus.Found, "Checked.", 63, 0, true, 0, Array.Empty<string>()));
    }
}
