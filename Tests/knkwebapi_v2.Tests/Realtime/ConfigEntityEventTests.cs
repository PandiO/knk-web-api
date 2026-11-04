using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Realtime;

/// <summary>
/// PandiO/knk-workspace#182 (S3, DESIGN §6.2–§6.3, decision Q3 option A): the entity interceptor and its map turn a
/// save into <c>EntityChanged</c> and the configuration events, only after the save succeeded and (inside a
/// transaction) after the commit; the gate services send <c>GateChanged</c> for definition edits only.
/// </summary>
public class ConfigEntityEventTests
{
    private sealed class RecordingSink : IRealtimeEventSink
    {
        public List<RealtimeEvent> Events { get; } = new();
        public void Publish(RealtimeEvent realtimeEvent) => Events.Add(realtimeEvent);
        public List<T> Payloads<T>(string type) => Events.Where(e => e.Type == type).Select(e => (T)e.Payload!).ToList();
    }

    private sealed class RecordingPublisher : IRealtimePublisher
    {
        public List<(string Type, object? Payload)> Game { get; } = new();

        public void PublishToGameServers(string type, object? payload, string? serverId = null, RealtimeActor? actor = null, int v = 1) =>
            Game.Add((type, payload));

        public void PublishToWebUser(int userId, string type, object? payload, RealtimeActor? actor = null, int v = 1) { }

        public void PublishToWebGroups(IReadOnlyCollection<string> groups, string type, object? payload, RealtimeActor? actor = null, int v = 1) { }

        public void PublishToConnection(RealtimeHubKind hub, string connectionId, string type, object? payload,
            RealtimeActor? actor = null, int v = 1) { }
    }

    private static RealtimeEntityChange Change(Type type, RealtimeEntityState state, IEnumerable<string>? modified = null,
        params (string Name, object? Value)[] values) =>
        new(type, state, (modified ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal),
            values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal));

    private static RealtimeEntityChange Modified(Type type, string[] modified, params (string Name, object? Value)[] values) =>
        Change(type, RealtimeEntityState.Modified, modified, values);

    // ===== the map =====

    [Fact]
    public void Map_AllowListedEntity_SendsEntityChangedWithItsKey()
    {
        var events = RealtimeEntityChangeMap.Map(new[]
        {
            Change(typeof(Town), RealtimeEntityState.Added, null, ("Id", 4), ("WgRegionId", "town_4")),
            Modified(typeof(ItemBlueprint), new[] { "Name" }, ("Id", 9)),
            Change(typeof(MenuTemplate), RealtimeEntityState.Deleted, null, ("Id", 2), ("Key", "main")),
        });

        var payloads = events.Where(e => e.Type == ConfigEventTypes.EntityChanged).Select(e => (EntityChangedPayload)e.Payload!).ToList();
        Assert.Contains(new EntityChangedPayload("Town", 4, EntityChangeOps.Created, "town_4"), payloads);
        Assert.Contains(new EntityChangedPayload("ItemBlueprint", 9, EntityChangeOps.Updated), payloads);
        Assert.Contains(new EntityChangedPayload("MenuTemplate", 2, EntityChangeOps.Deleted, "main"), payloads);
    }

    [Fact]
    public void Map_TypeOutsideTheAllowList_SendsNothing()
    {
        Assert.Empty(RealtimeEntityChangeMap.Map(new[] { Modified(typeof(Street), new[] { "Name" }, ("Id", 1)) }));
        Assert.False(RealtimeEntityChangeMap.IsWatched(typeof(Street)));
        Assert.False(RealtimeEntityChangeMap.IsWatched(typeof(GateDoor)));
    }

    [Fact]
    public void Map_UserPresenceOrPasswordOnly_SendsNothing_OtherFieldsDo()
    {
        Assert.Empty(RealtimeEntityChangeMap.Map(new[]
        {
            Modified(typeof(User), new[] { nameof(User.IsOnline), nameof(User.LastSeenAt) }, ("Id", 3), ("Uuid", "u")),
            Modified(typeof(User), new[] { nameof(User.PasswordHash), nameof(User.TokenVersion) }, ("Id", 3), ("Uuid", "u")),
        }));

        var events = RealtimeEntityChangeMap.Map(new[]
        {
            Modified(typeof(User), new[] { nameof(User.Username), nameof(User.LastSeenAt) }, ("Id", 3), ("Uuid", "u")),
        });
        Assert.Equal(new EntityChangedPayload("User", 3, EntityChangeOps.Updated, "u"), Assert.Single(events).Payload);
    }

    [Fact]
    public void Map_ManyOfOneType_CollapsesToOneEventWithoutAnId()
    {
        var changes = Enumerable.Range(1, RealtimeEntityChangeMap.CollapseAbove + 1)
            .Select(i => Modified(typeof(MinecraftMaterialRef), new[] { "IconUrl" }, ("Id", i)));

        var only = Assert.Single(RealtimeEntityChangeMap.Map(changes));
        Assert.Equal(new EntityChangedPayload("MinecraftMaterialRef", null, EntityChangeOps.Updated), only.Payload);
    }

    [Fact]
    public void Map_SameEntityTwice_SendsOnce_DeletedWins()
    {
        var events = RealtimeEntityChangeMap.Map(new[]
        {
            Modified(typeof(Grade), new[] { "Name" }, ("Id", 5)),
            Change(typeof(Grade), RealtimeEntityState.Deleted, null, ("Id", 5)),
        });
        Assert.Equal(new EntityChangedPayload("Grade", 5, EntityChangeOps.Deleted), Assert.Single(events).Payload);
    }

    [Fact]
    public void Map_DomainAccess_OnlyForEntryExitOrRegion()
    {
        Assert.DoesNotContain(RealtimeEntityChangeMap.Map(new[] { Modified(typeof(District), new[] { "Description" }, ("Id", 8)) }),
            e => e.Type == ConfigEventTypes.DomainAccessChanged);

        var events = RealtimeEntityChangeMap.Map(new[]
        {
            Modified(typeof(District), new[] { nameof(Domain.AllowEntry) }, ("Id", 8)),
            Modified(typeof(Town), new[] { nameof(Domain.WgRegionId) }, ("Id", 2)),
            Change(typeof(Structure), RealtimeEntityState.Added, null, ("Id", 11)),
            Change(typeof(DomainSubRegion), RealtimeEntityState.Deleted, null, ("Id", 30), ("DomainId", 8)),
        });
        var domains = events.Where(e => e.Type == ConfigEventTypes.DomainAccessChanged)
            .Select(e => ((DomainAccessChangedPayload)e.Payload!).DomainId).ToList();
        Assert.Equal(new[] { 8, 2, 11 }, domains); // the sub-region's domain 8 is sent once
    }

    [Fact]
    public void Map_DomainTeleportNavigationAndHub()
    {
        var events = RealtimeEntityChangeMap.Map(new[]
        {
            Modified(typeof(Town), new[] { nameof(Domain.TeleportPriceGems) }, ("Id", 2), ("TeleportEnabled", true)),
            Modified(typeof(Town), new[] { nameof(Domain.RoadAccessOverride) }, ("Id", 2), ("TeleportEnabled", false)),
            Modified(typeof(Town), new[] { nameof(Domain.IsHub) }, ("Id", 2), ("TeleportEnabled", false)),
        });
        Assert.Contains(events, e => e.Type == ConfigEventTypes.TeleportDestinationsChanged);
        Assert.Contains(events, e => e.Type == ConfigEventTypes.NavigationSettingsChanged);
        Assert.Contains(events, e => e.Type == ConfigEventTypes.GameSettingsChanged
            && ((ConfigChangedPayload)e.Payload!).Scope == ConfigScopes.Hub);

        // A renamed domain without a warp is not a teleport change.
        Assert.DoesNotContain(RealtimeEntityChangeMap.Map(new[]
            {
                Modified(typeof(Town), new[] { nameof(Domain.Name) }, ("Id", 2), ("TeleportEnabled", false)),
            }), e => e.Type == ConfigEventTypes.TeleportDestinationsChanged);
    }

    [Fact]
    public void Map_GameSettings_RuntimeWorldsReportAloneSendsNothing()
    {
        Assert.Empty(RealtimeEntityChangeMap.Map(new[]
        {
            Modified(typeof(GameSettings), new[] { nameof(GameSettings.RuntimeWorldsJson), nameof(GameSettings.UpdatedAt) }),
        }));
        var events = RealtimeEntityChangeMap.Map(new[]
        {
            Modified(typeof(GameSettings), new[] { nameof(GameSettings.RuntimeWorldsJson), nameof(GameSettings.WorldSettingsJson) }),
        });
        Assert.Equal(new ConfigChangedPayload(ConfigScopes.Game), Assert.Single(events).Payload);
    }

    [Fact]
    public void Map_ConfigEntities_OneEventPerScope()
    {
        var events = RealtimeEntityChangeMap.Map(new[]
        {
            Change(typeof(SiegeSpawnpoint), RealtimeEntityState.Added, null, ("Id", 1)),
            Change(typeof(SiegeSpawnpoint), RealtimeEntityState.Added, null, ("Id", 2)),
            Modified(typeof(SiegeLobby), new[] { "CooldownSeconds" }, ("Id", 1)),
            Modified(typeof(LootboxSpawnArea), new[] { "MaxActive" }, ("Id", 1)),
            Change(typeof(TelemetryEnhancedTarget), RealtimeEntityState.Deleted, null, ("Id", 1)),
            Modified(typeof(NavigationCompassSettings), new[] { "Enabled" }, ("Id", 1)),
        });
        Assert.Equal(new[]
        {
            (ConfigEventTypes.SiegeConfigChanged, ConfigScopes.Scenario),
            (ConfigEventTypes.SiegeConfigChanged, ConfigScopes.Lobby),
            (ConfigEventTypes.LootboxConfigChanged, ConfigScopes.SpawnArea),
            (ConfigEventTypes.TelemetryConfigChanged, ConfigScopes.EnhancedTargets),
            (ConfigEventTypes.NavigationSettingsChanged, ConfigScopes.Compass),
        }, events.Select(e => (e.Type, ((ConfigChangedPayload)e.Payload!).Scope)).ToArray());
        Assert.False(RealtimeEntityChangeMap.IsWatched(typeof(SiegeMatch)));
        Assert.False(RealtimeEntityChangeMap.IsWatched(typeof(LootboxSpawn)));
    }

    [Fact]
    public void Map_Roads_ByWorld_DirtyFlagAloneSendsNothing()
    {
        Assert.Empty(RealtimeEntityChangeMap.Map(new[] { Modified(typeof(RoadTile), new[] { nameof(RoadTile.Dirty) }, ("World", "world")) }));

        var events = RealtimeEntityChangeMap.Map(new[]
        {
            Modified(typeof(RoadTile), new[] { nameof(RoadTile.Version) }, ("World", "world")),
            Change(typeof(RoadNode), RealtimeEntityState.Added, null, ("World", "world")),
            Change(typeof(RoadEdge), RealtimeEntityState.Added, null, ("World", "world_nether")),
            Modified(typeof(RoadProfile), new[] { "Name" }, ("Id", 1)),
        });
        Assert.Equal(new string?[] { "world", "world_nether", null },
            events.Select(e => ((RoadNetworkChangedPayload)e.Payload!).World).ToArray());
    }

    // ===== the interceptor on a real context =====

    private sealed class Fixture : IDisposable
    {
        public RecordingPublisher Publisher { get; } = new();
        public RealtimeTransactionEvents Transactions { get; } = new();
        public bool InTransaction { get; set; }
        public KnKDbContext Db { get; }

        public Fixture()
        {
            var interceptor = new RealtimeEntityChangeInterceptor(context =>
                new AfterCommitRealtimeEventSink(Publisher, Transactions, context, inTransaction: _ => InTransaction));
            Db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
                .UseInMemoryDatabase("entity-events-" + Guid.NewGuid())
                .AddInterceptors(interceptor)
                .Options);
        }

        public void Dispose() => Db.Dispose();
    }

    private static Town NewTown(string region) => new()
    {
        Name = "Town " + region, Description = "d", WgRegionId = region, AllowEntry = true, AllowExit = true,
    };

    [Fact]
    public async Task Interceptor_AfterTheSave_SendsWithTheGeneratedId()
    {
        using var f = new Fixture();
        var town = NewTown("town_a");
        f.Db.Add(town);
        Assert.Empty(f.Publisher.Game);

        await f.Db.SaveChangesAsync();

        Assert.True(town.Id > 0);
        Assert.Contains(f.Publisher.Game, g => g.Type == ConfigEventTypes.EntityChanged
            && Equals(g.Payload, new EntityChangedPayload("Town", town.Id, EntityChangeOps.Created, "town_a")));
        Assert.Contains(f.Publisher.Game, g => g.Type == ConfigEventTypes.DomainAccessChanged
            && Equals(g.Payload, new DomainAccessChangedPayload(town.Id)));
    }

    [Fact]
    public async Task Interceptor_ModifiedProperties_DecideTheEvents_AndDeleteIsSent()
    {
        using var f = new Fixture();
        var town = NewTown("town_b");
        f.Db.Add(town);
        await f.Db.SaveChangesAsync();
        f.Publisher.Game.Clear();

        town.Description = "new text";
        await f.Db.SaveChangesAsync();
        Assert.Equal(new[] { ConfigEventTypes.EntityChanged }, f.Publisher.Game.Select(g => g.Type).ToArray());

        f.Publisher.Game.Clear();
        town.AllowExit = false;
        await f.Db.SaveChangesAsync();
        Assert.Contains(f.Publisher.Game, g => g.Type == ConfigEventTypes.DomainAccessChanged);

        f.Publisher.Game.Clear();
        f.Db.Remove(town);
        await f.Db.SaveChangesAsync();
        Assert.Contains(f.Publisher.Game, g => g.Type == ConfigEventTypes.EntityChanged
            && ((EntityChangedPayload)g.Payload!).Op == EntityChangeOps.Deleted);
    }

    [Fact]
    public async Task Interceptor_SaveWithoutWatchedEntities_SendsNothing()
    {
        using var f = new Fixture();
        f.Db.Add(new Street { Name = "Main" });
        await f.Db.SaveChangesAsync();
        Assert.Empty(f.Publisher.Game);
    }

    [Fact]
    public async Task Interceptor_InsideATransaction_WaitsForTheCommit_AndARollbackDrops()
    {
        using var f = new Fixture();
        f.InTransaction = true;
        f.Db.Add(NewTown("town_c"));
        await f.Db.SaveChangesAsync();
        Assert.Empty(f.Publisher.Game);
        Assert.True(f.Transactions.HeldCount(f.Db) > 0);

        f.Transactions.Committed(f.Db);
        Assert.Contains(f.Publisher.Game, g => g.Type == ConfigEventTypes.EntityChanged);

        f.Publisher.Game.Clear();
        f.Db.Add(NewTown("town_d"));
        await f.Db.SaveChangesAsync();
        f.Transactions.Discarded(f.Db);
        f.Transactions.Committed(f.Db);
        Assert.Empty(f.Publisher.Game);
    }

    // ===== gates: definition edits only =====

    private static GateDoorService DoorService(Mock<IGateDoorRepository> repo, IRealtimeEventSink sink) =>
        new(repo.Object, new Mock<IGateStructureRepository>().Object, new Mock<ILocationRepository>().Object,
            new Mock<ILocationService>().Object, new Mock<IMapper>().Object, sink);

    [Fact]
    public async Task GateDoor_DefinitionEdits_SendGateChangedForTheStructure()
    {
        var repo = new Mock<IGateDoorRepository>();
        repo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new GateDoor { Id = 7, GateStructureId = 3 });
        var sink = new RecordingSink();
        var service = DoorService(repo, sink);

        await service.UpdateRegionDataAsync(7, isOpenedRegion: false, regionData: "{}");
        await service.ClearBlockSnapshotsAsync(7);
        await service.DeleteAsync(7);

        Assert.Equal(new[] { 3, 3, 3 }, sink.Payloads<GateChangedPayload>(ConfigEventTypes.GateChanged).Select(p => p.GateStructureId).ToArray());
    }

    [Fact]
    public async Task GateDoor_RuntimeWritesOfTheGameServer_SendNothing()
    {
        var repo = new Mock<IGateDoorRepository>();
        repo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new GateDoor { Id = 7, GateStructureId = 3, HealthMax = 500 });
        var sink = new RecordingSink();
        var service = DoorService(repo, sink);

        await service.UpdateHealthAsync(7, 100);
        await service.UpdateStateAsync(7, GateDoorOpenState.OPEN, isDestroyed: false);
        await service.UpdateOperationalSettingsAsync(7, isActive: true, isInvincible: false);

        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task GateStructure_DeleteAndOverrides_SendGateChanged()
    {
        var repo = new Mock<IGateStructureRepository>();
        repo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(new GateStructure { Id = 3, Name = "g", WgRegionId = "gate_3" });
        repo.Setup(r => r.IsReferencedBySiegeAsync(3)).ReturnsAsync(false);
        var sink = new RecordingSink();
        var service = new GateStructureService(repo.Object, new Mock<ILocationRepository>().Object,
            new Mock<ILocationService>().Object, new Mock<IMapper>().Object, new Mock<ITeleportDestinationService>().Object,
            new Mock<IDomainRegionNameFinalizer>().Object, knkwebapi_v2.Tests.Services.FixedWorldResolver.Instance, sink);

        await service.UpdateOverridesAsync(3, new GateStructureOverridesUpdateDto { IsInvincibleOverride = true });
        await service.DeleteAsync(3);

        Assert.Equal(new[] { EntityChangeOps.Updated, EntityChangeOps.Deleted },
            sink.Payloads<GateChangedPayload>(ConfigEventTypes.GateChanged).Select(p => p.Op).ToArray());
    }
}
