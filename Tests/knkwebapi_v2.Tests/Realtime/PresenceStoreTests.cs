using Xunit;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime.Presence;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Tests.Realtime;

/// <summary>PandiO/knk-workspace#178: hub presence written to User.IsOnline/LastSeenAt (DESIGN §5).</summary>
public class PresenceStoreTests : IDisposable
{
    private const string A = "00000000-0000-0000-0000-00000000000a";
    private const string B = "00000000-0000-0000-0000-00000000000b";
    private const string C = "00000000-0000-0000-0000-00000000000c";

    private static readonly DateTime Earlier = new(2026, 10, 25, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private readonly KnKDbContext _db = new(new DbContextOptionsBuilder<KnKDbContext>()
        .UseInMemoryDatabase("presence-" + Guid.NewGuid()).Options);

    public void Dispose() => _db.Dispose();

    private async Task<User> AddUser(string name, string? uuid, bool online)
    {
        var user = new User { Username = name, Uuid = uuid, IsOnline = online, LastSeenAt = Earlier, CreatedAt = Earlier };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<User> Reload(int id)
    {
        _db.ChangeTracker.Clear();
        return await _db.Users.SingleAsync(u => u.Id == id);
    }

    [Fact]
    public async Task SetOnline_MovesLastSeenOnlyForPlayersThatWereOffline()
    {
        var offline = await AddUser("a", A, online: false);
        var online = await AddUser("b", B, online: true);

        var changed = await new PresenceStore(_db).SetOnlineAsync(new[] { A.ToUpperInvariant(), B, C }, Now);

        Assert.Equal(1, changed);
        var a = await Reload(offline.Id);
        Assert.True(a.IsOnline);
        Assert.Equal(Now, a.LastSeenAt);
        var b = await Reload(online.Id);
        Assert.True(b.IsOnline);
        Assert.Equal(Earlier, b.LastSeenAt);
    }

    [Fact]
    public async Task SetOffline_MarksOnlinePlayersOffline()
    {
        var online = await AddUser("a", A, online: true);
        var other = await AddUser("b", B, online: true);

        var changed = await new PresenceStore(_db).SetOfflineAsync(new[] { A }, Now);

        Assert.Equal(1, changed);
        var a = await Reload(online.Id);
        Assert.False(a.IsOnline);
        Assert.Equal(Now, a.LastSeenAt);
        Assert.True((await Reload(other.Id)).IsOnline);
    }

    [Fact]
    public async Task Reconcile_SetsExactlyTheRosterOnline()
    {
        // b was left online by a crash; c is in the roster but still offline; a stays online; d has no uuid.
        var a = await AddUser("a", A, online: true);
        var b = await AddUser("b", B, online: true);
        var c = await AddUser("c", C, online: false);
        var d = await AddUser("d", null, online: true);

        var changed = await new PresenceStore(_db).ReconcileAsync(new HashSet<string> { A, C }, Now);

        Assert.Equal(3, changed);
        Assert.True((await Reload(a.Id)).IsOnline);
        Assert.Equal(Earlier, (await Reload(a.Id)).LastSeenAt);
        Assert.False((await Reload(b.Id)).IsOnline);
        Assert.Equal(Now, (await Reload(b.Id)).LastSeenAt);
        Assert.True((await Reload(c.Id)).IsOnline);
        Assert.False((await Reload(d.Id)).IsOnline);
    }

    [Fact]
    public async Task EmptyInput_ChangesNothing()
    {
        var store = new PresenceStore(_db);
        Assert.Equal(0, await store.SetOnlineAsync(Array.Empty<string>(), Now));
        Assert.Equal(0, await store.SetOfflineAsync(new[] { " " }, Now));
    }
}
