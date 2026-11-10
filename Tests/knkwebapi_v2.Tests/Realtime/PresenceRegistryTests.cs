using Xunit;
using knkwebapi_v2.Realtime.Presence;
using knkwebapi_v2.Tests.Services.Email;

namespace knkwebapi_v2.Tests.Realtime;

/// <summary>PandiO/knk-workspace#178: the in-memory presence registry (DESIGN §5).</summary>
public class PresenceRegistryTests
{
    private const string A = "00000000-0000-0000-0000-00000000000a";
    private const string B = "00000000-0000-0000-0000-00000000000b";
    private const string C = "00000000-0000-0000-0000-00000000000c";

    private readonly ManualClock _clock = new(new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc));

    private PresenceRegistry NewRegistry() => new(_clock);

    private static RosterEntry P(string uuid, string? world = "world") => new(uuid, "p" + uuid[^1], world, null);

    [Fact]
    public void Roster_ReplacesPlayersAndReportsWhoLeft()
    {
        var registry = NewRegistry();
        registry.ServerConnected("s1", "c1");
        registry.ReplaceRoster("s1", new[] { P(A), P(B) });

        var change = registry.ReplaceRoster("s1", new[] { P(B), P(C) });

        Assert.Equal(new[] { B, C }, change.Online.OrderBy(u => u));
        Assert.Equal(new[] { A }, change.GoneOffline);
        Assert.Equal("s1", registry.ServerOf(C));
        Assert.Null(registry.ServerOf(A));
    }

    [Fact]
    public void Uuids_AreNormalizedToLowerCase()
    {
        var registry = NewRegistry();
        registry.ReplaceRoster("s1", new[] { P(A.ToUpperInvariant()) });

        Assert.Equal("s1", registry.ServerOf(A));
        Assert.Contains(A, registry.AllOnlineUuids());
    }

    [Fact]
    public void PlayerLeft_IsOfflineOnlyWhenOnNoOtherServer()
    {
        var registry = NewRegistry();
        registry.PlayerJoined("s1", P(A));
        registry.PlayerJoined("s2", P(A));

        Assert.False(registry.PlayerLeft("s1", A));
        Assert.True(registry.PlayerLeft("s2", A));
        Assert.Empty(registry.AllOnlineUuids());
    }

    [Fact]
    public void DisconnectedServer_KeepsPlayersDuringGrace_ThenExpires()
    {
        var registry = NewRegistry();
        registry.ServerConnected("s1", "c1");
        registry.ReplaceRoster("s1", new[] { P(A), P(B) });
        registry.ServerDisconnected("s1", "c1");

        _clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Empty(registry.ExpireDisconnected(TimeSpan.FromSeconds(60)));
        Assert.Equal("s1", registry.ServerOf(A));

        _clock.Advance(TimeSpan.FromSeconds(1));
        var expired = Assert.Single(registry.ExpireDisconnected(TimeSpan.FromSeconds(60)));
        Assert.Equal("s1", expired.ServerId);
        Assert.Equal(new[] { A, B }, expired.OfflineUuids.OrderBy(u => u));
        Assert.Null(registry.ServerOf(A));
        Assert.Empty(registry.Servers());
    }

    [Fact]
    public void ReconnectWithinGrace_StopsTheGracePeriod()
    {
        var registry = NewRegistry();
        registry.ServerConnected("s1", "c1");
        registry.ReplaceRoster("s1", new[] { P(A) });
        registry.ServerDisconnected("s1", "c1");
        _clock.Advance(TimeSpan.FromSeconds(30));

        Assert.True(registry.ServerConnected("s1", "c2"));
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Empty(registry.ExpireDisconnected(TimeSpan.FromSeconds(60)));
        Assert.Equal("s1", registry.ServerOf(A));
    }

    [Fact]
    public void LateDisconnectOfAReplacedConnection_IsIgnored()
    {
        var registry = NewRegistry();
        registry.ServerConnected("s1", "c1");
        registry.ServerConnected("s1", "c2");
        registry.ReplaceRoster("s1", new[] { P(A) });

        registry.ServerDisconnected("s1", "c1");
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Empty(registry.ExpireDisconnected(TimeSpan.FromSeconds(60)));
        Assert.Contains(("s1", true, 1), registry.Servers());
    }

    [Fact]
    public void ExpiredServer_DoesNotTakeOfflinePlayersOnlineElsewhere()
    {
        var registry = NewRegistry();
        registry.ServerConnected("s1", "c1");
        registry.ReplaceRoster("s1", new[] { P(A), P(B) });
        registry.ServerConnected("s2", "c2");
        registry.ReplaceRoster("s2", new[] { P(B) });
        registry.ServerDisconnected("s1", "c1");
        _clock.Advance(TimeSpan.FromMinutes(2));

        var expired = Assert.Single(registry.ExpireDisconnected(TimeSpan.FromSeconds(60)));

        Assert.Equal(new[] { A }, expired.OfflineUuids);
        Assert.Equal("s2", registry.ServerOf(B));
    }

    [Fact]
    public void BlankUuids_AreIgnored()
    {
        var registry = NewRegistry();
        registry.ReplaceRoster("s1", new[] { new RosterEntry(" ", null, null, null) });

        Assert.Equal(string.Empty, registry.PlayerJoined("s1", new RosterEntry("", null, null, null)));
        Assert.False(registry.PlayerLeft("s1", ""));
        Assert.Empty(registry.AllOnlineUuids());
    }
}
