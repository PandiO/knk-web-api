using System.Security.Claims;
using System.Text.Json;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Map;
using knkwebapi_v2.Tests.Services.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using static knkwebapi_v2.Tests.Services.Map.MapPlayerRegistryTests;

namespace knkwebapi_v2.Tests.Services.Map;

/// <summary>
/// Overview map L4 (PandiO/knk-workspace#738, OVERVIEW_MAP_DESIGN § 5 rules 6-9, O07, O13, O21, O22, O24, O31; tier:3):
/// the three audience snapshots. Every audience's serialized response body is checked: a player in staff or owner mode
/// or in a running match never appears in the anonymous or logged-in body (A12, A14), and appears tagged for a viewer
/// who may see hidden players. Also A11 (position), A13 (AFK), A16 (quit) and one build per tick (O21).
/// </summary>
public sealed class MapPlayerSnapshotsTests
{
    private const string Plain = "00000000-0000-0000-0000-0000000000a1";
    private const string Staff = "00000000-0000-0000-0000-0000000000b2";
    private const string Owner = "00000000-0000-0000-0000-0000000000c3";
    private const string Sieging = "00000000-0000-0000-0000-0000000000d4";
    private const string Weird = "00000000-0000-0000-0000-0000000000e5";

    private readonly ManualClock _clock = new(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc));
    private readonly MapOptions _options = new() { Enabled = true, LivePlayers = new MapLivePlayersOptions { Enabled = true } };
    private readonly MapPlayerRegistry _registry;
    private readonly FakeData _data = new();

    public MapPlayerSnapshotsTests()
    {
        _registry = new MapPlayerRegistry(_clock);
    }

    private sealed class FakeData : IMapLivePlayerData
    {
        public int LabelReads;
        public int ArenaReads;

        public Task<IReadOnlyDictionary<string, MapPlayerLabelDto>> LabelsAsync(IReadOnlyCollection<string> uuids, CancellationToken ct = default)
        {
            LabelReads += uuids.Count;
            IReadOnlyDictionary<string, MapPlayerLabelDto> result = uuids.ToDictionary(u => u,
                u => new MapPlayerLabelDto { Name = "Name-" + u[^2..], BracketColor = "&2", TextColor = "&a" });
            return Task.FromResult(result);
        }

        public Task<List<MapArenaDto>> ArenasAsync(IReadOnlyList<MapPlayerEntry> live, CancellationToken ct = default)
        {
            ArenaReads++;
            var count = live.Count(p => p.Minigame == "siege");
            var arenas = count == 0 ? new List<MapArenaDto>() : new List<MapArenaDto>
            {
                new()
                {
                    Minigame = "Siege", Label = MapPlayerLabels.ArenaLabel("Siege", count), PlayerCount = count,
                    Pin = new MapPinDto { World = "world", X = 30, Y = 64, Z = 30, Source = "location" },
                },
            };
            return Task.FromResult(arenas);
        }
    }

    private sealed class Monitor : IOptionsMonitor<MapOptions>
    {
        public Monitor(MapOptions value) => CurrentValue = value;
        public MapOptions CurrentValue { get; }
        public MapOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<MapOptions, string?> listener) => null;
    }

    private MapPlayerSnapshots Snapshots()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMapLivePlayerData>(_ => _data);
        var provider = services.BuildServiceProvider();
        return new MapPlayerSnapshots(_registry, provider.GetRequiredService<IServiceScopeFactory>(), new Monitor(_options),
            NullLogger<MapPlayerSnapshots>.Instance, _clock);
    }

    private void ReportEveryone()
    {
        _registry.Apply("s1", Report(
            Player(Plain, x: 1),
            Player(Staff, mode: "staff"),
            Player(Owner, mode: "owner", afk: true),
            Player(Sieging, minigame: "siege", matchId: 500),
            Player(Weird, mode: "vanish")));
    }

    private static string Body(MapPlayersDto dto) => JsonSerializer.Serialize(dto);

    [Theory]
    [InlineData(MapAudience.Anonymous)]
    [InlineData(MapAudience.LoggedIn)]
    public async Task PublicAudiences_NeverReceiveModeOrMatchPlayers_NotEvenInTheBody(MapAudience audience) // A12, A14, rules 7-8
    {
        ReportEveryone();

        var dto = await Snapshots().GetAsync(audience);
        var body = Body(dto);

        Assert.Equal(new[] { Plain }, dto.Players.Select(p => p.Uuid));
        foreach (var hidden in new[] { Staff, Owner, Sieging, Weird })
        {
            Assert.DoesNotContain(hidden, body);
            Assert.DoesNotContain("Name-" + hidden[^2..], body);
        }
        Assert.DoesNotContain("\"staff\"", body);
        Assert.DoesNotContain("\"owner\"", body);
        Assert.Null(dto.Players[0].Mode);
        Assert.Null(dto.Players[0].Minigame);
    }

    [Fact]
    public async Task MaySeeHidden_GetsEveryoneTagged() // A12, O07
    {
        ReportEveryone();

        var dto = await Snapshots().GetAsync(MapAudience.MaySeeHidden);

        var byUuid = dto.Players.ToDictionary(p => p.Uuid);
        Assert.Equal(5, byUuid.Count);
        Assert.Equal(((string?)null, (string?)null), (byUuid[Plain].Mode, byUuid[Plain].Minigame));
        Assert.Equal("staff", byUuid[Staff].Mode);
        Assert.Equal("owner", byUuid[Owner].Mode);
        Assert.Equal("hidden", byUuid[Weird].Mode);
        Assert.Equal("Siege", byUuid[Sieging].Minigame);
        Assert.True(byUuid[Owner].Afk);
    }

    [Fact]
    public async Task ArenaPin_IsTheSameForEveryAudience() // A14, O24, O31
    {
        ReportEveryone();
        var snapshots = Snapshots();

        foreach (var audience in Enum.GetValues<MapAudience>())
        {
            var arena = Assert.Single((await snapshots.GetAsync(audience)).Arenas);
            Assert.Equal(("Siege", "[Siege] 1 player", 1), (arena.Minigame, arena.Label, arena.PlayerCount));
            Assert.Equal((30.0, 30.0), (arena.Pin.X, arena.Pin.Z));
        }
    }

    [Fact]
    public async Task PlainPlayer_ShowsWithPositionLabelAndNoAccountFields() // A11, O23, § 9.2
    {
        _registry.Apply("s1", Report(Player(Plain, x: 12.5)));

        var dto = await Snapshots().GetAsync(MapAudience.Anonymous);

        var p = Assert.Single(dto.Players);
        Assert.Equal(("world", 12.5, 64.0, -5.0, 90), (p.World, p.X, p.Y, p.Z, p.Yaw));
        Assert.Equal("Name-a1", p.Label.Name);
        Assert.Equal(2000, dto.IntervalMs);
        using var json = JsonDocument.Parse(Body(dto));
        var names = json.RootElement.GetProperty("players")[0].EnumerateObject().Select(o => o.Name).OrderBy(n => n);
        Assert.Equal(new[] { "afk", "label", "minigame", "mode", "uuid", "world", "x", "y", "yaw", "z" }, names);
    }

    [Fact]
    public async Task OneBuildPerTick_ForAllAudiences_ThenMovementAndAfkShowNextTick() // O21, A11, A13
    {
        _registry.Apply("s1", Report(Player(Plain, x: 1)));
        var snapshots = Snapshots();

        await snapshots.GetAsync(MapAudience.Anonymous);
        await snapshots.GetAsync(MapAudience.LoggedIn);
        await snapshots.GetAsync(MapAudience.MaySeeHidden);
        Assert.Equal(1, snapshots.Builds);

        _registry.Apply("s1", Report(Player(Plain, x: 21, afk: true)));
        _clock.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Equal(1.0, (await snapshots.GetAsync(MapAudience.Anonymous)).Players[0].X);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        var moved = (await snapshots.GetAsync(MapAudience.Anonymous)).Players[0];
        Assert.Equal((21.0, true), (moved.X, moved.Afk));
        Assert.Equal(2, snapshots.Builds);
    }

    [Fact]
    public async Task Labels_AreCachedPerPlayer() // § 9.1: cheap per tick
    {
        _registry.Apply("s1", Report(Player(Plain)));
        var snapshots = Snapshots();

        for (var i = 0; i < 5; i++)
        {
            await snapshots.GetAsync(MapAudience.Anonymous);
            _clock.Advance(TimeSpan.FromSeconds(2));
            _registry.Apply("s1", Report(Player(Plain)));
        }

        Assert.Equal(1, _data.LabelReads);
        Assert.Equal(5, _data.ArenaReads);
    }

    [Fact]
    public async Task QuitPlayer_IsGoneWithinThreeIntervals() // A16
    {
        _registry.Apply("s1", Report(Player(Plain)));
        var snapshots = Snapshots();
        Assert.Single((await snapshots.GetAsync(MapAudience.Anonymous)).Players);

        _clock.Advance(TimeSpan.FromMilliseconds(6001));

        Assert.Empty((await snapshots.GetAsync(MapAudience.Anonymous)).Players);
    }

    [Fact]
    public async Task EachAudienceGetsItsOwnObjects()
    {
        ReportEveryone();
        var snapshots = Snapshots();

        var anonymous = await snapshots.GetAsync(MapAudience.Anonymous);
        var hidden = await snapshots.GetAsync(MapAudience.MaySeeHidden);

        Assert.NotSame(anonymous.Players[0], hidden.Players.Single(p => p.Uuid == Plain));
        Assert.NotSame(anonymous.Players[0].Label, hidden.Players.Single(p => p.Uuid == Plain).Label);
        Assert.NotSame(anonymous.Arenas[0], hidden.Arenas[0]);
    }

    // ===== Audience (O22) =====

    private static ClaimsPrincipal LoggedIn(int id) =>
        new(new ClaimsIdentity(new[] { new Claim("uid", id.ToString()) }, "test"));

    [Fact]
    public async Task Audience_AnonymousLoggedInOrMaySeeHidden() // O22
    {
        var permissions = new Mock<IPermissionResolutionService>();
        permissions.Setup(p => p.CheckAnyAsync(7, It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync((int _, IReadOnlyList<string> nodes) => nodes.Contains("knk.mode.staff") ? "knk.mode.staff" : null);
        permissions.Setup(p => p.CheckAnyAsync(8, It.IsAny<IReadOnlyList<string>>())).ReturnsAsync((string?)null);
        var cache = new PermissionReadCache();

        Assert.Equal(MapAudience.Anonymous, await MapPlayerSnapshots.AudienceOfAsync(new ClaimsPrincipal(), null, permissions.Object, cache));
        Assert.Equal(MapAudience.Anonymous, await MapPlayerSnapshots.AudienceOfAsync(null, null, permissions.Object, cache));
        Assert.Equal(MapAudience.MaySeeHidden, await MapPlayerSnapshots.AudienceOfAsync(LoggedIn(7), null, permissions.Object, cache));
        Assert.Equal(MapAudience.LoggedIn, await MapPlayerSnapshots.AudienceOfAsync(LoggedIn(8), null, permissions.Object, cache));
        Assert.Equal(new[] { "knk.mode.staff", "knk.mode.owner" }, MapPlayerSnapshots.SeeHiddenNodes);
    }
}
