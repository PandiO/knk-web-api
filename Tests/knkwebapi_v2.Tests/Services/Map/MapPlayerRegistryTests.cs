using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Services.Map;
using knkwebapi_v2.Tests.Services.Email;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Map;

/// <summary>
/// Overview map L4 (PandiO/knk-workspace#738, OVERVIEW_MAP_DESIGN § 5 rule 6, § 6.1.3, A11, A16): the latest report per
/// player, dropped on <c>PlayerLeft</c> or after three missed intervals.
/// </summary>
public sealed class MapPlayerRegistryTests
{
    public const string A = "00000000-0000-0000-0000-00000000000A";
    public const string B = "00000000-0000-0000-0000-00000000000b";

    private readonly ManualClock _clock = new(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc));

    public static MapPlayerReportDto Player(string uuid, double x = 0, string mode = "none", bool afk = false,
        string? minigame = null, long? matchId = null) => new()
    {
        Uuid = uuid, World = "world", X = x, Y = 64, Z = -5, Yaw = 90.4, Afk = afk, Mode = mode, Minigame = minigame, MatchId = matchId,
    };

    public static MapPlayersReportDto Report(params MapPlayerReportDto[] players) => new() { IntervalMs = 2000, Players = players.ToList() };

    [Fact]
    public void Apply_KeepsTheLatestReportPerPlayer_LowerCaseUuid() // A11
    {
        var registry = new MapPlayerRegistry(_clock);

        Assert.Equal(2, registry.Apply("s1", Report(Player(A, x: 1), Player(B))));
        registry.Apply("s1", Report(Player(A, x: 21)));

        var live = registry.Live();
        Assert.Equal(2, live.Count);
        var a = live.Single(p => p.Uuid == A.ToLowerInvariant());
        Assert.Equal((21.0, 90, "world", "s1"), (a.X, a.Yaw, a.World, a.ServerId));
    }

    [Fact]
    public void Live_DropsAPlayerAfterThreeMissedIntervals() // A16, § 5 rule 6
    {
        var registry = new MapPlayerRegistry(_clock);
        registry.Apply("s1", Report(Player(A)));

        _clock.Advance(TimeSpan.FromMilliseconds(6000));
        Assert.Single(registry.Live());

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Empty(registry.Live());
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Remove_OnPlayerLeft_DropsAtOnce() // A16
    {
        var registry = new MapPlayerRegistry(_clock);
        registry.Apply("s1", Report(Player(A), Player(B)));

        Assert.True(registry.Remove(A));
        Assert.False(registry.Remove("not-a-uuid-at-all"));
        Assert.Equal(new[] { B }, registry.Live().Select(p => p.Uuid));
    }

    [Fact]
    public void Apply_RejectsInvalidEntries()
    {
        var registry = new MapPlayerRegistry(_clock);
        var badWorld = Player(A);
        badWorld.World = " ";
        var nan = Player(B);
        nan.X = double.NaN;

        Assert.Equal(0, registry.Apply("s1", Report(Player("nope"), badWorld, nan)));
        Assert.Equal(0, registry.Apply("s1", null));
        Assert.Equal(0, registry.Apply("s1", new MapPlayersReportDto { IntervalMs = 2000 }));
    }

    [Fact]
    public void Apply_ModesAndMinigames_UnknownModeCountsAsHidden() // § 5 rules 7, 8
    {
        var registry = new MapPlayerRegistry(_clock);
        registry.Apply("s1", Report(Player(A, mode: "Staff"), Player(B, mode: "invisible")));
        registry.Apply("s1", Report(Player("00000000-0000-0000-0000-00000000000c", minigame: " SIEGE ", matchId: 5)));
        registry.Apply("s1", Report(Player("00000000-0000-0000-0000-00000000000d", matchId: 5)));

        var live = registry.Live().ToDictionary(p => p.Uuid[^1]);
        Assert.Equal(MapPlayerMode.Staff, live['a'].Mode);
        Assert.Equal(MapPlayerMode.Unknown, live['b'].Mode);
        Assert.True(live['b'].HiddenFromPublic);
        Assert.Equal(("siege", (long?)5), (live['c'].Minigame, live['c'].MatchId));
        Assert.True(live['c'].HiddenFromPublic);
        Assert.Equal((null, (long?)null), (live['d'].Minigame, live['d'].MatchId)); // a match id without a minigame is ignored
        Assert.False(live['d'].HiddenFromPublic);
    }

    [Fact]
    public void Interval_IsClamped_SoAReportCannotStayForever()
    {
        var registry = new MapPlayerRegistry(_clock);
        registry.Apply("s1", new MapPlayersReportDto { IntervalMs = int.MaxValue, Players = new() { Player(A) } });

        _clock.Advance(TimeSpan.FromMilliseconds(MapPlayerRegistry.MaxIntervalMs * 3 + 1));
        Assert.Empty(registry.Live());
    }
}
