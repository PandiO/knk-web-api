using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Realtime;

/// <summary>
/// PandiO/knk-workspace#183 (S4, DESIGN §6.5, §9): the web hub's live updates. The web map turns a save into
/// <c>EntityChanged</c> for the dashboard types and <c>PresenceChanged</c>, each gated by the node that gates the same data
/// over REST; the dispatcher sends a node-gated message only to holders; the game servers' events are unchanged.
/// </summary>
public class WebLiveEventTests
{
    private static RealtimeEntityChange Change(Type type, RealtimeEntityState state, IEnumerable<string>? modified = null,
        params (string Name, object? Value)[] values) =>
        new(type, state, (modified ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal),
            values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal));

    private static List<(string Node, WebEntityChangedPayload Payload)> Entities(IEnumerable<RealtimeEvent> events) =>
        events.Where(e => e.Type == WebLiveEventTypes.EntityChanged)
            .Select(e => (e.WebNode!, (WebEntityChangedPayload)e.Payload!)).ToList();

    private static List<PresenceChangedPayload> Presence(IEnumerable<RealtimeEvent> events) =>
        events.Where(e => e.Type == WebLiveEventTypes.PresenceChanged).Select(e => (PresenceChangedPayload)e.Payload!).ToList();

    // ===== the map =====

    [Fact]
    public void Map_DashboardType_SendsEntityChangedToItsAreaStaff_WithTheActor()
    {
        var events = RealtimeWebChangeMap.Map(new[]
        {
            Change(typeof(Street), RealtimeEntityState.Modified, new[] { "Name" }, ("Id", 3)),
            Change(typeof(ItemBlueprint), RealtimeEntityState.Added, null, ("Id", 9)),
            Change(typeof(LootboxType), RealtimeEntityState.Deleted, null, ("Id", 2)),
        }, actorUserId: 7, actorName: "alice");

        Assert.All(events, e => Assert.Null(e.WebGroups));
        Assert.Equal(new[]
        {
            // Dashboard access model (PandiO/knk-workspace#280 decision 8): each type's area node.
            (StaffPermissions.ManageWorld, new WebEntityChangedPayload("Street", 3, EntityChangeOps.Updated, 7, "alice")),
            (StaffPermissions.ManageContent, new WebEntityChangedPayload("ItemBlueprint", 9, EntityChangeOps.Created, 7, "alice")),
            (StaffPermissions.ManageLootboxes, new WebEntityChangedPayload("LootboxType", 2, EntityChangeOps.Deleted, 7, "alice")),
        }, Entities(events));
    }

    [Fact]
    public void Map_SubType_AlsoSendsForItsBaseDashboardType()
    {
        Assert.Equal(new[] { "Town", "Domain" }, RealtimeWebChangeMap.DashboardTypeNames(typeof(Town)));

        var payloads = Entities(RealtimeWebChangeMap.Map(new[]
        {
            Change(typeof(Town), RealtimeEntityState.Modified, new[] { "Description" }, ("Id", 4)),
        })).Select(e => e.Payload).ToList();

        Assert.Equal(new[] { ("Town", 4), ("Domain", 4) }, payloads.Select(p => (p.Type, p.Id ?? 0)));
        Assert.All(payloads, p => Assert.Null(p.ActorUserId));
    }

    [Fact]
    public void Map_UsersAndTheirPermissionRows_GoToUserManagersOnly()
    {
        var events = Entities(RealtimeWebChangeMap.Map(new[]
        {
            Change(typeof(User), RealtimeEntityState.Modified, new[] { nameof(User.Username) }, ("Id", 5)),
            Change(typeof(PermissionGrant), RealtimeEntityState.Added, null, ("Id", 1)),
            Change(typeof(UserPermissionGroup), RealtimeEntityState.Added, null, ("Id", 2)),
            Change(typeof(AuditLogEntry), RealtimeEntityState.Added, null, ("Id", 3)),
        }));

        Assert.Equal(4, events.Count);
        Assert.All(events, e => Assert.Equal(StaffPermissions.ManageUsers, e.Node));
    }

    [Fact]
    public void Map_PlayerPrivateOrLedgerTypes_AreNeverSent()
    {
        foreach (var type in RealtimeWebChangeMap.NotSent)
        {
            Assert.False(RealtimeWebChangeMap.IsWatched(type));
            Assert.Null(RealtimeWebChangeMap.NodeFor(type));
            Assert.Empty(RealtimeWebChangeMap.Map(new[] { Change(type, RealtimeEntityState.Added, null, ("Id", 1)) }));
        }
        Assert.Contains(typeof(UserIgnore), RealtimeWebChangeMap.NotSent);
        Assert.Contains(typeof(CurrencyTransaction), RealtimeWebChangeMap.NotSent);
    }

    [Fact]
    public void Map_TypeTheDashboardCannotShow_SendsNothing()
    {
        Assert.False(RealtimeWebChangeMap.IsWatched(typeof(GameSettings)));
        Assert.Empty(RealtimeWebChangeMap.Map(new[]
        {
            Change(typeof(GameSettings), RealtimeEntityState.Modified, new[] { "AllowPvp" }, ("Id", 1)),
        }));
    }

    [Fact]
    public void Map_PresenceOnlyUserChange_SendsPresenceChanged_NotEntityChanged()
    {
        var seen = new DateTime(2026, 11, 5, 20, 0, 0, DateTimeKind.Utc);
        var events = RealtimeWebChangeMap.Map(new[]
        {
            Change(typeof(User), RealtimeEntityState.Modified, new[] { nameof(User.IsOnline), nameof(User.LastSeenAt) },
                ("Id", 5), ("Uuid", "u-5"), (nameof(User.IsOnline), true), (nameof(User.LastSeenAt), seen)),
            Change(typeof(User), RealtimeEntityState.Modified, new[] { nameof(User.IsOnline), nameof(User.LastSeenAt) },
                ("Id", 6), ("Uuid", "u-6"), (nameof(User.IsOnline), false), (nameof(User.LastSeenAt), seen)),
        }, serverOf: uuid => uuid == "u-5" ? "survival" : "other");

        Assert.Empty(Entities(events));
        Assert.All(events, e => Assert.Equal(StaffPermissions.ManageUsers, e.WebNode));
        Assert.Equal(new[]
        {
            new PresenceChangedPayload(5, true, "survival", seen),
            new PresenceChangedPayload(6, false, null, seen),
        }, Presence(events));
    }

    [Fact]
    public void Map_LastSeenAlone_OrAuthColumns_SendNothing()
    {
        Assert.Empty(RealtimeWebChangeMap.Map(new[]
        {
            Change(typeof(User), RealtimeEntityState.Modified, new[] { nameof(User.LastSeenAt) }, ("Id", 5), (nameof(User.IsOnline), true)),
            Change(typeof(User), RealtimeEntityState.Modified, new[] { nameof(User.PasswordHash), nameof(User.TokenVersion) }, ("Id", 5)),
            Change(typeof(Street), RealtimeEntityState.Modified, Array.Empty<string>(), ("Id", 1)),
        }));
    }

    [Fact]
    public void Map_ManyOfOneTypeInOneSave_CollapsesToOneEventWithoutAnId()
    {
        var many = Enumerable.Range(1, RealtimeWebChangeMap.CollapseAbove + 1)
            .Select(i => Change(typeof(Street), RealtimeEntityState.Added, null, ("Id", i)));
        var events = Entities(RealtimeWebChangeMap.Map(many));
        Assert.Equal(new WebEntityChangedPayload("Street", null, EntityChangeOps.Updated, null, null), Assert.Single(events).Payload);

        var roster = Enumerable.Range(1, RealtimeWebChangeMap.CollapseAbove + 1)
            .Select(i => Change(typeof(User), RealtimeEntityState.Modified, new[] { nameof(User.IsOnline) }, ("Id", i), (nameof(User.IsOnline), false)));
        Assert.Equal(new PresenceChangedPayload(null, null, null, null), Assert.Single(Presence(RealtimeWebChangeMap.Map(roster))));
    }

    [Fact]
    public void Map_SameEntityTwice_SentOnce_DeleteWins_AndARowWithoutAnIdMeansReread()
    {
        var events = Entities(RealtimeWebChangeMap.Map(new[]
        {
            Change(typeof(Street), RealtimeEntityState.Modified, new[] { "Name" }, ("Id", 3)),
            Change(typeof(Street), RealtimeEntityState.Deleted, null, ("Id", 3)),
            Change(typeof(ItemBlueprintTag), RealtimeEntityState.Added, null),
        }));
        Assert.Contains(events, e => e.Payload == new WebEntityChangedPayload("Street", 3, EntityChangeOps.Deleted, null, null));
        Assert.Contains(events, e => e.Payload == new WebEntityChangedPayload("ItemBlueprintTag", null, EntityChangeOps.Updated, null, null));
        Assert.Equal(2, events.Count);
    }

    // ===== the sink and publisher =====

    [Fact]
    public async Task Sink_WebNodeEvent_IsQueuedForThatNode_NotForTheGameServers()
    {
        var publisher = new RealtimePublisher(Options.Create(new RealtimeOptions()), NullLogger<RealtimePublisher>.Instance);
        var sink = new AfterCommitRealtimeEventSink(publisher, new RealtimeTransactionEvents());

        sink.Publish(RealtimeEvent.ToWebHolders(StaffPermissions.ManageContent, WebLiveEventTypes.EntityChanged,
            new WebEntityChangedPayload("Street", 1, EntityChangeOps.Updated, 7, "alice"), 7));

        Assert.True(publisher.Reader.TryRead(out var message));
        Assert.Equal((RealtimeHubKind.Web, null, null, StaffPermissions.ManageContent),
            (message!.Hub, message.Group, message.ConnectionId, message.RequiredNode));
        Assert.Equal(7, message.Actor!.UserId);
        Assert.False(publisher.Reader.TryRead(out _));
        await Task.CompletedTask;
    }

    // ===== the dispatcher: holders only =====

    private sealed class RecordingTransport : IRealtimeTransport
    {
        public List<(RealtimeHubKind Hub, string ConnectionId, RealtimeEnvelope Envelope)> Sent { get; } = new();

        public Task SendAsync(RealtimeHubKind hub, string connectionId, RealtimeEnvelope envelope, CancellationToken cancellationToken)
        {
            Sent.Add((hub, connectionId, envelope));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAudience : IRealtimeWebAudience
    {
        public HashSet<(int, string)> Holders { get; } = new();
        public int Checks { get; private set; }

        public ValueTask<bool> HoldsAsync(int userId, string node, CancellationToken cancellationToken = default)
        {
            Checks++;
            return ValueTask.FromResult(Holders.Contains((userId, node)));
        }
    }

    private static RealtimePublisher NewPublisher() =>
        new(Options.Create(new RealtimeOptions()), NullLogger<RealtimePublisher>.Instance);

    private static RealtimeMessage NodeMessage(string node, string type = "EntityChanged") =>
        new(RealtimeHubKind.Web, null, null, type, 1, new { }, null, DateTime.UtcNow, null, node);

    [Fact]
    public async Task Dispatch_NodeGatedMessage_ReachesOnlyHoldersWebConnections()
    {
        var connections = new RealtimeConnections();
        connections.Add(new RealtimeConnection("staff-1", RealtimeHubKind.Web, new[] { RealtimeGroups.User(1) }, userId: 1));
        connections.Add(new RealtimeConnection("staff-1b", RealtimeHubKind.Web, new[] { RealtimeGroups.User(1) }, userId: 1));
        connections.Add(new RealtimeConnection("player-2", RealtimeHubKind.Web, new[] { RealtimeGroups.User(2) }, userId: 2));
        connections.Add(new RealtimeConnection("game", RealtimeHubKind.Game, new[] { RealtimeGroups.Servers }, "s1"));
        var audience = new FakeAudience();
        audience.Holders.Add((1, StaffPermissions.ManageContent));
        var transport = new RecordingTransport();
        var dispatcher = new RealtimeDispatchService(NewPublisher(), connections, transport,
            NullLogger<RealtimeDispatchService>.Instance, audience: audience);

        Assert.Equal(2, await dispatcher.DispatchAsync(NodeMessage(StaffPermissions.ManageContent)));
        Assert.Equal(new[] { "staff-1", "staff-1b" }, transport.Sent.Select(s => s.ConnectionId).OrderBy(c => c));
        Assert.All(transport.Sent, s => Assert.Equal(RealtimeHubKind.Web, s.Hub));

        transport.Sent.Clear();
        Assert.Equal(0, await dispatcher.DispatchAsync(NodeMessage(StaffPermissions.ManageUsers, "PresenceChanged")));
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task Dispatch_NodeGatedMessage_WithoutAnAudience_ReachesNobody()
    {
        var connections = new RealtimeConnections();
        connections.Add(new RealtimeConnection("w1", RealtimeHubKind.Web, new[] { RealtimeGroups.User(1) }, userId: 1));
        var transport = new RecordingTransport();
        var dispatcher = new RealtimeDispatchService(NewPublisher(), connections, transport, NullLogger<RealtimeDispatchService>.Instance);

        Assert.Equal(0, await dispatcher.DispatchAsync(NodeMessage(StaffPermissions.ManageContent)));
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task Dispatch_UngatedMessages_DoNotAskTheAudience()
    {
        var connections = new RealtimeConnections();
        connections.Add(new RealtimeConnection("w1", RealtimeHubKind.Web, new[] { RealtimeGroups.User(1) }, userId: 1));
        var audience = new FakeAudience();
        var transport = new RecordingTransport();
        var dispatcher = new RealtimeDispatchService(NewPublisher(), connections, transport,
            NullLogger<RealtimeDispatchService>.Instance, audience: audience);

        await dispatcher.DispatchAsync(new RealtimeMessage(RealtimeHubKind.Web, RealtimeGroups.User(1), null, "WorldTaskUpdated", 1, null, null, DateTime.UtcNow));

        Assert.Single(transport.Sent);
        Assert.Equal(0, audience.Checks);
    }

    // ===== the audience: the REST check, cached =====

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (RealtimeWebAudience Audience, Mock<IPermissionResolutionService> Permissions, ManualTime Time) NewAudience(int cacheSeconds = 30)
    {
        var permissions = new Mock<IPermissionResolutionService>();
        var services = new ServiceCollection().AddSingleton(permissions.Object).BuildServiceProvider();
        var time = new ManualTime();
        var audience = new RealtimeWebAudience(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RealtimeOptions { WebPermissionCacheSeconds = cacheSeconds }), time);
        return (audience, permissions, time);
    }

    private static PermissionCheckResponseDto Answer(bool allowed) =>
        new() { Result = allowed ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared };

    [Fact]
    public async Task Audience_AsksThePermissionService_AndReusesTheAnswerUntilItExpires()
    {
        var (audience, permissions, time) = NewAudience();
        permissions.Setup(p => p.CheckAsync(1, StaffPermissions.ManageContent)).ReturnsAsync(Answer(true));

        Assert.True(await audience.HoldsAsync(1, StaffPermissions.ManageContent));
        permissions.Setup(p => p.CheckAsync(1, StaffPermissions.ManageContent)).ReturnsAsync(Answer(false));
        Assert.True(await audience.HoldsAsync(1, StaffPermissions.ManageContent)); // cached
        permissions.Verify(p => p.CheckAsync(1, StaffPermissions.ManageContent), Times.Once);

        time.Now = time.Now.AddSeconds(31);
        Assert.False(await audience.HoldsAsync(1, StaffPermissions.ManageContent)); // the node was taken away
    }

    [Fact]
    public async Task Audience_UnknownUserOrFailedCheck_IsNo_AndAFailureIsNotCached()
    {
        var (audience, permissions, _) = NewAudience();
        permissions.Setup(p => p.CheckAsync(9, It.IsAny<string>())).ReturnsAsync((PermissionCheckResponseDto?)null);
        Assert.False(await audience.HoldsAsync(9, StaffPermissions.ManageUsers));
        Assert.False(await audience.HoldsAsync(0, StaffPermissions.ManageUsers));

        permissions.Setup(p => p.CheckAsync(3, StaffPermissions.ManageUsers)).ThrowsAsync(new InvalidOperationException("db down"));
        Assert.False(await audience.HoldsAsync(3, StaffPermissions.ManageUsers));
        permissions.Setup(p => p.CheckAsync(3, StaffPermissions.ManageUsers)).ReturnsAsync(Answer(true));
        Assert.True(await audience.HoldsAsync(3, StaffPermissions.ManageUsers));
    }

    // ===== the interceptor on a real context =====

    private sealed class RecordingPublisher : IRealtimePublisher
    {
        public List<(string Type, object? Payload)> Game { get; } = new();
        public List<(string Node, string Type, object? Payload, RealtimeActor? Actor)> Web { get; } = new();

        public void PublishToGameServers(string type, object? payload, string? serverId = null, RealtimeActor? actor = null, int v = 1) =>
            Game.Add((type, payload));

        public void PublishToWebUser(int userId, string type, object? payload, RealtimeActor? actor = null, int v = 1) { }

        public void PublishToWebGroups(IReadOnlyCollection<string> groups, string type, object? payload, RealtimeActor? actor = null, int v = 1) { }

        public void PublishToWebHolders(string node, string type, object? payload, RealtimeActor? actor = null, int v = 1) =>
            Web.Add((node, type, payload, actor));

        public void PublishToConnection(RealtimeHubKind hub, string connectionId, string type, object? payload,
            RealtimeActor? actor = null, int v = 1) { }
    }

    private sealed class Fixture : IDisposable
    {
        public RecordingPublisher Publisher { get; } = new();
        public bool WebListening { get; set; } = true;
        public KnKDbContext Db { get; }

        public Fixture()
        {
            var interceptor = new RealtimeEntityChangeInterceptor(context =>
                    new AfterCommitRealtimeEventSink(Publisher, new RealtimeTransactionEvents(), context, inTransaction: _ => false),
                webListening: () => WebListening, serverOf: _ => "test-1");
            Db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
                .UseInMemoryDatabase("web-live-" + Guid.NewGuid())
                .AddInterceptors(interceptor)
                .Options);
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task Interceptor_DashboardTypeSave_SendsWebEntityChanged_AndTheGameServersGetWhatTheyGotBefore()
    {
        using var f = new Fixture();
        var street = new Street { Name = "Main" };
        f.Db.Add(street);
        await f.Db.SaveChangesAsync();

        Assert.Empty(f.Publisher.Game); // a street was never a game-server event
        var web = Assert.Single(f.Publisher.Web);
        Assert.Equal((StaffPermissions.ManageWorld, WebLiveEventTypes.EntityChanged), (web.Node, web.Type));
        Assert.Equal(new WebEntityChangedPayload("Street", street.Id, EntityChangeOps.Created, null, null), web.Payload);
        Assert.Equal("system", web.Actor!.Source);

        f.Publisher.Web.Clear();
        var town = new Town { Name = "T", Description = "d", WgRegionId = "town_x", AllowEntry = true, AllowExit = true };
        f.Db.Add(town);
        await f.Db.SaveChangesAsync();
        Assert.Contains(f.Publisher.Game, g => Equals(g.Payload, new EntityChangedPayload("Town", town.Id, EntityChangeOps.Created, "town_x")));
        Assert.Equal(new[] { "Town", "Domain" }, f.Publisher.Web.Select(w => ((WebEntityChangedPayload)w.Payload!).Type));
    }

    [Fact]
    public async Task Interceptor_PresenceWrite_SendsPresenceChangedToUserManagers_AndNothingToTheGameServers()
    {
        using var f = new Fixture();
        var user = new User { Username = "bob", Uuid = "00000000-0000-0000-0000-0000000000b2", CreatedAt = DateTime.UtcNow };
        f.Db.Add(user);
        await f.Db.SaveChangesAsync();
        f.Publisher.Game.Clear();
        f.Publisher.Web.Clear();

        user.IsOnline = true;
        user.LastSeenAt = new DateTime(2026, 11, 5, 21, 0, 0, DateTimeKind.Utc);
        await f.Db.SaveChangesAsync();

        Assert.Empty(f.Publisher.Game);
        var web = Assert.Single(f.Publisher.Web);
        Assert.Equal((StaffPermissions.ManageUsers, WebLiveEventTypes.PresenceChanged), (web.Node, web.Type));
        Assert.Equal(new PresenceChangedPayload(user.Id, true, "test-1", user.LastSeenAt), web.Payload);
    }

    [Fact]
    public async Task Interceptor_NoWebConnectionOpen_BuildsNoWebEvents()
    {
        using var f = new Fixture { WebListening = false };
        f.Db.Add(new Street { Name = "Quiet" });
        await f.Db.SaveChangesAsync();
        Assert.Empty(f.Publisher.Web);
        Assert.Empty(f.Publisher.Game);
    }

    // Developer decision 2026-11-06 on LX1-10: Realtime:WebLiveUpdatesEnabled switches the web live updates off.

    private static (RealtimeEntityChangeInterceptor Interceptor, RealtimeConnections Connections, RealtimePublisher Publisher)
        FromSetup(bool? enabled)
    {
        var settings = new Dictionary<string, string?>();
        if (enabled != null) settings["Realtime:WebLiveUpdatesEnabled"] = enabled.Value ? "true" : "false";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddKnkRealtime(configuration);
        var provider = services.BuildServiceProvider();
        var connections = provider.GetRequiredService<RealtimeConnections>();
        connections.Add(new RealtimeConnection("web-1", RealtimeHubKind.Web, new[] { RealtimeGroups.User(7) }, userId: 7));
        return (provider.GetRequiredService<RealtimeEntityChangeInterceptor>(), connections,
            provider.GetRequiredService<RealtimePublisher>());
    }

    private static async Task<(List<RealtimeMessage> Messages, int TownId)> SaveTownAndPresence(RealtimeEntityChangeInterceptor interceptor,
        RealtimePublisher publisher)
    {
        using var db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase("web-live-off-" + Guid.NewGuid())
            .AddInterceptors(interceptor)
            .Options);
        var town = new Town { Name = "T", Description = "d", WgRegionId = "town_off", AllowEntry = true, AllowExit = true };
        db.Add(town);
        var user = new User { Username = "carol", Uuid = "00000000-0000-0000-0000-0000000000c3", CreatedAt = DateTime.UtcNow };
        db.Add(user);
        await db.SaveChangesAsync();
        user.IsOnline = true;
        await db.SaveChangesAsync();
        publisher.Complete();
        var messages = new List<RealtimeMessage>();
        await foreach (var message in publisher.Reader.ReadAllAsync())
        {
            messages.Add(message);
        }
        return (messages, town.Id);
    }

    [Fact]
    public void WebLiveUpdatesEnabled_DefaultsToOn()
    {
        Assert.True(new RealtimeOptions().WebLiveUpdatesEnabled);
    }

    [Fact]
    public async Task Setup_WebLiveUpdatesOff_BuildsNoWebEvents_TheGameServersStillGetTheirs()
    {
        var (interceptor, _, publisher) = FromSetup(enabled: false);

        var (messages, townId) = await SaveTownAndPresence(interceptor, publisher);

        Assert.DoesNotContain(messages, m => m.Hub == RealtimeHubKind.Web);
        Assert.DoesNotContain(messages, m => m.Type == WebLiveEventTypes.PresenceChanged);
        Assert.Contains(messages, m => m.Hub == RealtimeHubKind.Game && m.Type == "EntityChanged"
            && Equals(m.Payload, new EntityChangedPayload("Town", townId, EntityChangeOps.Created, "town_off")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task Setup_WebLiveUpdatesOnOrUnset_SendsEntityChangedAndPresenceChangedToTheWeb(bool? enabled)
    {
        var (interceptor, _, publisher) = FromSetup(enabled);

        var (messages, _) = await SaveTownAndPresence(interceptor, publisher);

        var web = messages.Where(m => m.Hub == RealtimeHubKind.Web).ToList();
        Assert.Contains(web, m => m.Type == WebLiveEventTypes.EntityChanged && m.RequiredNode == StaffPermissions.ManageWorld);
        Assert.Contains(web, m => m.Type == WebLiveEventTypes.PresenceChanged && m.RequiredNode == StaffPermissions.ManageUsers);
        Assert.Contains(messages, m => m.Hub == RealtimeHubKind.Game && m.Type == "EntityChanged");
    }

    // ===== dashboard access model: live updates follow the area (PandiO/knk-workspace#280 decision 8) =====

    [Theory]
    [InlineData(typeof(Category), StaffPermissions.ManageContent)]
    [InlineData(typeof(MinecraftMaterialRef), StaffPermissions.ManageContent)]
    [InlineData(typeof(Town), StaffPermissions.ManageWorld)]
    [InlineData(typeof(GateDoor), StaffPermissions.ManageWorld)]
    [InlineData(typeof(Kit), StaffPermissions.ManageKits)]
    [InlineData(typeof(KitContent), StaffPermissions.ManageKits)]
    [InlineData(typeof(LootboxSpawnArea), StaffPermissions.ManageLootboxes)]
    [InlineData(typeof(SiegeTeam), StaffPermissions.SiegeManage)]
    [InlineData(typeof(PermissionGroup), StaffPermissions.ManageUsers)]
    [InlineData(typeof(User), StaffPermissions.ManageUsers)]
    [InlineData(typeof(PermissionGrant), StaffPermissions.ManageUsers)]
    [InlineData(typeof(AuditLogEntry), StaffPermissions.ManageUsers)]
    public void NodeFor_IsTheTypesAreaNode(Type entityType, string node)
    {
        Assert.Equal(node, RealtimeWebChangeMap.NodeFor(entityType));
    }

    [Fact]
    public void NodeFor_NotSentTypes_StaysNull()
    {
        Assert.Null(RealtimeWebChangeMap.NodeFor(typeof(ItemInstance)));
        Assert.Null(RealtimeWebChangeMap.NodeFor(typeof(KitClaim)));
    }

    [Fact]
    public void EveryWatchedDashboardType_HasAnAreaNode_NotTheFallback()
    {
        var types = typeof(Town).Assembly.GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(FormConfigurableEntityAttribute), false).Length > 0)
            .Where(RealtimeWebChangeMap.IsWatched);
        Assert.All(types, t => Assert.Equal(DashboardAccess.Find(t.Name)!.AreaNode, RealtimeWebChangeMap.NodeFor(t)));
    }

    [Fact]
    public void Map_ADeleteGoesToWhoeverViewsTheType_NotToADeleteNode()
    {
        var events = RealtimeWebChangeMap.Map(new[] { Change(typeof(Kit), RealtimeEntityState.Deleted, null, ("Id", 5)) });
        Assert.Equal(new[] { (StaffPermissions.ManageKits, new WebEntityChangedPayload("Kit", 5, EntityChangeOps.Deleted, null, null)) },
            Entities(events));
    }
}
