using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Arena duels Phase 1 (KNG-210, docs/specs/arena-duels/DESIGN.md §4.1–4.2): arena, battleground and spectator-area
/// CRUD with the design defaults, the settings validation, eligible kits in order, the region finalizer's parents
/// (District → Arena → children), the model's delete rules, discovery types and the arena_2 seed.
/// </summary>
public class ArenaServiceTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly Mock<IRegionService> _regions = new();
    private readonly List<(string Old, string New, string? Type, string? Parent)> _renames = new();
    private readonly ArenaService _service;

    public ArenaServiceTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?, string?>((o, n, t, p, _) => _renames.Add((o, n, t, p)))
            .ReturnsAsync(true);
        var mapper = new MapperConfiguration(c =>
        {
            c.AddProfile<ArenaMappingProfile>();
            c.AddProfile<PagedQueryMappingProfile>();
            c.AddProfile<LocationMappingProfile>();
        }).CreateMapper();
        _service = new ArenaService(
            new ArenaRepository(_context),
            mapper,
            new Mock<ITeleportDestinationService>().Object,
            new DomainRegionNameFinalizer(_context, _regions.Object, NullLogger<DomainRegionNameFinalizer>.Instance),
            new DomainWorldResolver(_context));
        SeedWorld();
    }

    public void Dispose() => _context.Dispose();

    private void SeedWorld()
    {
        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "town_1", WorldName = "world" });
        _context.Districts.Add(new District { Id = 10, Name = "Old Town", Description = "d", WgRegionId = "domain_10", TownId = 1, WorldName = "world" });
        _context.Streets.Add(new Street { Id = 5, Name = "Arena Way" });
        _context.Locations.AddRange(
            new Location { Id = 100, Name = "lobby", World = "world" },
            new Location { Id = 101, Name = "exit", World = "world" },
            new Location { Id = 102, Name = "spawn A", World = "world" },
            new Location { Id = 103, Name = "spawn B", World = "world" },
            new Location { Id = 104, Name = "nether", World = "world_nether" });
        _context.Kits.AddRange(new Kit { Id = 7, Name = "Knight" }, new Kit { Id = 8, Name = "Archer" });
        _context.TitleBrackets.Add(new TitleBracket { Id = 3, MaleName = "Peasant", FemaleName = "Peasant" });
        _context.SaveChanges();
    }

    private static ArenaDto NewArena(Action<ArenaDto>? change = null)
    {
        var dto = new ArenaDto
        {
            Name = "Arena",
            Key = "arena_2",
            Description = "the arena",
            WgRegionId = "arena_2",
            DistrictId = 10,
            StreetId = 5,
            LobbyLocationId = 100
        };
        change?.Invoke(dto);
        return dto;
    }

    // ===== CRUD and defaults =====

    [Fact]
    public async Task Create_StoresTheDesignDefaults()
    {
        var created = await _service.CreateAsync(NewArena());

        var arena = await _context.Arenas.SingleAsync();
        Assert.Equal(created.Id, arena.Id);
        Assert.True(arena.IsDuelEnabled);
        Assert.Equal(3, arena.MaxQueuedMatches);
        Assert.Equal(3, arena.MaxOffers);
        Assert.Equal(60, arena.OfferSeconds);
        Assert.True(arena.AllowCoinStakes);
        Assert.True(arena.AllowItemStakes);
        Assert.Equal(10_000, arena.MaxCoinStake);
        Assert.Equal(9, arena.MaxItemStacks);
        Assert.Equal(50, arena.MinStakeRatioPercent);
        Assert.Equal(5, arena.FeePercent);
        Assert.Null(arena.StakeMinAccountAgeHours);
        Assert.Null(arena.StakeMinTitleBracketId);
        Assert.Equal(3, arena.MaxStakedDuelsPerPairPerDay);
        Assert.Equal(10, arena.PairCooldownMinutes);
        Assert.Equal(25_000, arena.DailyCoinWinCap);
        Assert.True(arena.AllowOwnGearKeep);
        Assert.False(arena.AllowOwnGearDrop);
        Assert.False(arena.AllowFixedKit);
        Assert.False(arena.AllowKitChoice);
        Assert.Equal(DuelGearMode.OWN_GEAR_KEEP, arena.DefaultGearMode);
        Assert.Equal(30, arena.WarmupSeconds);
        Assert.Equal(10, arena.CountdownSeconds);
        Assert.Equal(300, arena.FightTimeLimitSeconds);
        Assert.Equal(180, arena.MaxWaitSeconds);
        Assert.Equal(5, arena.WinnerLingerSeconds);
        Assert.True(arena.AnnounceMatches);
        Assert.True(arena.AnnounceFriendly);
        Assert.Equal("world", arena.WorldName);
        Assert.Equal("OWN_GEAR_KEEP", created.DefaultGearMode);
    }

    [Fact]
    public async Task Update_ANullSettingKeepsItsValue_AndAValueChangesIt()
    {
        var created = await _service.CreateAsync(NewArena(d => d.MaxOffers = 2));

        await _service.UpdateAsync(created.Id!.Value, NewArena(d => d.FeePercent = 10));

        var arena = await _context.Arenas.AsNoTracking().SingleAsync();
        Assert.Equal(2, arena.MaxOffers);
        Assert.Equal(10, arena.FeePercent);
    }

    [Fact]
    public async Task GetByKey_IsCaseInsensitive_AndCarriesTheChildren()
    {
        var created = await _service.CreateAsync(NewArena());
        await _service.CreateBattlegroundAsync(NewBattleground(created.Id!.Value, "bg1"));
        await _service.CreateSpectatorAreaAsync(new ArenaSpectatorAreaDto { ArenaId = created.Id, Name = "Stands", WgRegionId = "stands" });

        var read = await _service.GetByKeyAsync("ARENA_2");

        Assert.NotNull(read);
        Assert.Single(read!.Battlegrounds!);
        Assert.Single(read.SpectatorAreas!);
        Assert.Single(await _service.GetAllAsync());
    }

    [Fact]
    public async Task Create_RefusesADuplicateKey()
    {
        await _service.CreateAsync(NewArena());
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.WgRegionId = "other")));
        Assert.Contains("key", ex.Message);
    }

    [Fact]
    public async Task Delete_RemovesTheArenaAndItsChildrenFromDomainsToo()
    {
        var created = await _service.CreateAsync(NewArena(d => { d.AllowFixedKit = true; d.EligibleKits = new() { new ArenaKitDto { KitId = 7 } }; }));
        var bg = await _service.CreateBattlegroundAsync(NewBattleground(created.Id!.Value, "bg1"));
        var stands = await _service.CreateSpectatorAreaAsync(new ArenaSpectatorAreaDto { ArenaId = created.Id, Name = "Stands", WgRegionId = "stands" });

        await _service.DeleteAsync(created.Id.Value);

        Assert.Empty(_context.Arenas);
        Assert.Empty(_context.ArenaKits);
        Assert.False(await _context.Domains.AnyAsync(d => d.Id == bg.Id || d.Id == stands.Id || d.Id == created.Id));
        Assert.True(await _context.Kits.AnyAsync(k => k.Id == 7));
        Assert.True(await _context.Locations.AnyAsync(l => l.Id == 100));
    }

    // ===== Validation (DESIGN §4.2) =====

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task MaxOffers_OutsideOneToSix_IsRefused(int value)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.MaxOffers = value)));
        Assert.Contains("MaxOffers", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task FeePercent_OutsideZeroToTwenty_IsRefused(int value)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.FeePercent = value)));
        Assert.Contains("FeePercent", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task MinStakeRatioPercent_OutsideZeroToHundred_IsRefused(int value)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.MinStakeRatioPercent = value)));
        Assert.Contains("MinStakeRatioPercent", ex.Message);
    }

    [Fact]
    public async Task FightTimeLimit_ZeroMeansNoLimit_NegativeIsRefused()
    {
        await _service.CreateAsync(NewArena(d => d.FightTimeLimitSeconds = 0));
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(NewArena(d => { d.Key = "a3"; d.WgRegionId = "a3"; d.FightTimeLimitSeconds = -1; })));
        Assert.Contains("FightTimeLimitSeconds", ex.Message);
    }

    [Fact]
    public async Task NoGearModeAllowed_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.AllowOwnGearKeep = false)));
        Assert.Contains("at least one gear mode", ex.Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AKitMode_NeedsAnEligibleKit(bool fixedKit, bool kitChoice)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(NewArena(d => { d.AllowFixedKit = fixedKit; d.AllowKitChoice = kitChoice; })));
        Assert.Contains("eligible kit", ex.Message);

        await _service.CreateAsync(NewArena(d =>
        {
            d.AllowFixedKit = fixedKit;
            d.AllowKitChoice = kitChoice;
            d.EligibleKits = new() { new ArenaKitDto { KitId = 7 } };
        }));
    }

    [Fact]
    public async Task DefaultGearMode_MustBeAllowed()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.DefaultGearMode = "OWN_GEAR_DROP")));
        Assert.Contains("default gear mode", ex.Message);

        await _service.CreateAsync(NewArena(d => { d.AllowOwnGearDrop = true; d.DefaultGearMode = "own_gear_drop"; }));
        Assert.Equal(DuelGearMode.OWN_GEAR_DROP, (await _context.Arenas.SingleAsync()).DefaultGearMode);
    }

    [Fact]
    public async Task AnUnknownGearMode_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.DefaultGearMode = "SWORDS")));
        Assert.Contains("DefaultGearMode", ex.Message);
    }

    [Fact]
    public async Task DuelsEnabled_NeedTheLobby_ButAnArenaWithDuelsOffDoesNot()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.LobbyLocationId = null)));
        Assert.Contains("lobby", ex.Message);

        await _service.CreateAsync(NewArena(d => { d.LobbyLocationId = null; d.IsDuelEnabled = false; }));
    }

    [Fact]
    public async Task ALobbyInAnotherWorld_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.LobbyLocationId = 104)));
        Assert.Contains("world", ex.Message);
    }

    [Fact]
    public async Task MissingReferences_AreRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.StakeMinTitleBracketId = 99)));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.ExitLocationId = 999)));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.DistrictId = 999)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(NewArena(d => { d.AllowFixedKit = true; d.EligibleKits = new() { new ArenaKitDto { KitId = 99 } }; })));
        Assert.Empty(_context.Arenas);
    }

    // ===== Eligible kits =====

    [Fact]
    public async Task EligibleKits_KeepTheListOrder_AndAreAReplaceSet()
    {
        var created = await _service.CreateAsync(NewArena(d =>
        {
            d.AllowKitChoice = true;
            d.EligibleKits = new() { new ArenaKitDto { KitId = 8 }, new ArenaKitDto { KitId = 7 } };
        }));
        var read = await _service.GetByIdAsync(created.Id!.Value);
        Assert.Equal(new int?[] { 8, 7 }, read!.EligibleKits!.Select(k => k.KitId));
        Assert.Equal("Archer", read.EligibleKits![0].KitName);

        await _service.UpdateAsync(created.Id.Value, NewArena(d =>
        {
            d.AllowKitChoice = true;
            d.EligibleKits = new() { new ArenaKitDto { KitId = 7 } };
        }));
        var rows = await _context.ArenaKits.AsNoTracking().ToListAsync();
        Assert.Equal((7, 0), (Assert.Single(rows).KitId, rows[0].SequenceNumber));
    }

    [Fact]
    public async Task RemovingTheLastKit_WhileAKitModeIsAllowed_IsRefused()
    {
        var created = await _service.CreateAsync(NewArena(d => { d.AllowFixedKit = true; d.EligibleKits = new() { new ArenaKitDto { KitId = 7 } }; }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAsync(created.Id!.Value, NewArena(d => d.EligibleKits = new())));
        // Not sending the list keeps it.
        await _service.UpdateAsync(created.Id!.Value, NewArena());
        Assert.Single(_context.ArenaKits);
    }

    [Fact]
    public async Task ADuplicateKit_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d =>
        {
            d.AllowFixedKit = true;
            d.EligibleKits = new() { new ArenaKitDto { KitId = 7 }, new ArenaKitDto { KitId = 7 } };
        })));
    }

    // ===== Battlegrounds and spectator areas =====

    private static ArenaBattlegroundDto NewBattleground(int arenaId, string key, Action<ArenaBattlegroundDto>? change = null)
    {
        var dto = new ArenaBattlegroundDto
        {
            ArenaId = arenaId,
            Name = "Pit " + key,
            Key = key,
            WgRegionId = "arena_2," + key + "-battleground",
            SpawnALocationId = 102,
            SpawnBLocationId = 103
        };
        change?.Invoke(dto);
        return dto;
    }

    [Fact]
    public async Task Battleground_DefaultsToNoEntry_AndTakesTheArenasWorld()
    {
        var arena = await _service.CreateAsync(NewArena());
        var bg = await _service.CreateBattlegroundAsync(NewBattleground(arena.Id!.Value, "bg1"));

        var stored = await _context.ArenaBattlegrounds.AsNoTracking().SingleAsync();
        Assert.False(stored.AllowEntry);
        Assert.True(stored.AllowExit);
        Assert.True(stored.IsEnabled);
        Assert.Equal("world", stored.WorldName);
        Assert.Equal("Arena", bg.ArenaName);
        Assert.False(new ArenaBattleground().AllowEntry);
    }

    [Fact]
    public async Task Battleground_KeyIsUniquePerArena()
    {
        var arena = await _service.CreateAsync(NewArena());
        await _service.CreateBattlegroundAsync(NewBattleground(arena.Id!.Value, "bg1"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateBattlegroundAsync(NewBattleground(arena.Id.Value, "BG1", d => d.WgRegionId = "other")));
    }

    [Fact]
    public async Task Battleground_SpawnsMustBeInItsOwnWorld()
    {
        var arena = await _service.CreateAsync(NewArena());
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateBattlegroundAsync(NewBattleground(arena.Id!.Value, "bg1", d => d.SpawnBLocationId = 104)));
        Assert.Contains("world", ex.Message);
    }

    [Fact]
    public async Task Battleground_EnabledNeedsBothSpawns_DisabledDoesNot()
    {
        var arena = await _service.CreateAsync(NewArena());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateBattlegroundAsync(NewBattleground(arena.Id!.Value, "bg1", d => d.SpawnBLocationId = null)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateBattlegroundAsync(NewBattleground(arena.Id!.Value, "bg1", d => d.SpawnBLocationId = 102)));
        await _service.CreateBattlegroundAsync(NewBattleground(arena.Id!.Value, "bg1", d => { d.SpawnBLocationId = null; d.IsEnabled = false; }));
    }

    [Fact]
    public async Task Battleground_NeedsAnExistingArena()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateBattlegroundAsync(NewBattleground(999, "bg1")));
    }

    [Fact]
    public async Task Battleground_UpdateAndDelete()
    {
        var arena = await _service.CreateAsync(NewArena());
        var bg = await _service.CreateBattlegroundAsync(NewBattleground(arena.Id!.Value, "bg1"));

        await _service.UpdateBattlegroundAsync(bg.Id!.Value, NewBattleground(arena.Id.Value, "bg1", d => { d.SortOrder = 4; d.IsEnabled = false; }));
        var stored = await _context.ArenaBattlegrounds.AsNoTracking().SingleAsync();
        Assert.Equal(4, stored.SortOrder);
        Assert.False(stored.IsEnabled);
        Assert.False(stored.AllowEntry);

        await _service.DeleteBattlegroundAsync(bg.Id.Value);
        Assert.False(await _context.Domains.AnyAsync(d => d.Id == bg.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteBattlegroundAsync(bg.Id.Value));
    }

    [Fact]
    public async Task SpectatorArea_IsAnOrdinaryDomain_ZeroOrMorePerArena()
    {
        var arena = await _service.CreateAsync(NewArena());
        Assert.Empty(await _service.GetSpectatorAreasAsync(arena.Id));

        await _service.CreateSpectatorAreaAsync(new ArenaSpectatorAreaDto { ArenaId = arena.Id, Name = "North stands", WgRegionId = "n", LocationId = 101 });
        await _service.CreateSpectatorAreaAsync(new ArenaSpectatorAreaDto { ArenaId = arena.Id, Name = "South stands", WgRegionId = "s" });

        var areas = await _service.GetSpectatorAreasAsync(arena.Id);
        Assert.Equal(2, areas.Count);
        Assert.All(areas, a => Assert.True(a.AllowEntry));
        Assert.Equal(101, areas[0].LocationId);
        Assert.Equal("exit", areas[0].Location!.Name);
    }

    [Fact]
    public async Task TheArenaReadCarriesItsLocationsForThePlugin()
    {
        var created = await _service.CreateAsync(NewArena(d => { d.LocationId = 101; d.ExitLocationId = 101; }));
        await _service.CreateBattlegroundAsync(NewBattleground(created.Id!.Value, "bg1"));

        var read = Assert.Single(await _service.GetAllAsync());

        Assert.Equal("exit", read.Location!.Name);
        Assert.Equal("lobby", read.LobbyLocation!.Name);
        Assert.Equal("exit", read.ExitLocation!.Name);
        Assert.Equal("spawn A", read.Battlegrounds![0].SpawnALocation!.Name);
        Assert.Equal("spawn B", read.Battlegrounds![0].SpawnBLocation!.Name);
    }

    // ===== Region finalizer: District → Arena → children =====

    [Fact]
    public async Task TempRegions_AreRenamedWithTheirTypeAndParent()
    {
        var arena = await _service.CreateAsync(NewArena(d => d.WgRegionId = "tempregion_worldtask_1"));
        var bg = await _service.CreateBattlegroundAsync(NewBattleground(arena.Id!.Value, "bg1", d => d.WgRegionId = "tempregion_worldtask_2"));
        var stands = await _service.CreateSpectatorAreaAsync(new ArenaSpectatorAreaDto { ArenaId = arena.Id, Name = "Stands", WgRegionId = "tempregion_worldtask_3" });

        Assert.Equal(new[]
        {
            ("tempregion_worldtask_1", $"domain_{arena.Id}", (string?)"Arena", (string?)"domain_10"),
            ("tempregion_worldtask_2", $"domain_{bg.Id}", "ArenaBattleground", $"domain_{arena.Id}"),
            ("tempregion_worldtask_3", $"domain_{stands.Id}", "ArenaSpectatorArea", $"domain_{arena.Id}")
        }, _renames);
        Assert.Equal($"domain_{bg.Id}", (await _context.ArenaBattlegrounds.AsNoTracking().SingleAsync()).WgRegionId);
    }

    [Fact]
    public void Finalizer_NamesTheArenaTypes()
    {
        Assert.Equal("Arena", DomainRegionNameFinalizer.DomainTypeOf(new Arena()));
        Assert.Equal("ArenaBattleground", DomainRegionNameFinalizer.DomainTypeOf(new ArenaBattleground()));
        Assert.Equal("ArenaSpectatorArea", DomainRegionNameFinalizer.DomainTypeOf(new ArenaSpectatorArea()));
    }

    [Fact]
    public async Task KitRepository_NamesTheArenasThatOfferAKit()
    {
        await _service.CreateAsync(NewArena(d => { d.AllowFixedKit = true; d.EligibleKits = new() { new ArenaKitDto { KitId = 7 } }; }));
        var kits = new KitRepository(_context);

        Assert.Equal(new[] { "Arena" }, await kits.ArenaNamesUsingAsync(7));
        Assert.Empty(await kits.ArenaNamesUsingAsync(8));
    }

    // ===== Model delete rules (become the migration's ON DELETE) =====

    public static IEnumerable<object[]> ExpectedDeleteRules() => new List<object[]>
    {
        new object[] { typeof(ArenaBattleground), nameof(ArenaBattleground.ArenaId), DeleteBehavior.Cascade },
        new object[] { typeof(ArenaSpectatorArea), nameof(ArenaSpectatorArea.ArenaId), DeleteBehavior.Cascade },
        new object[] { typeof(ArenaKit), nameof(ArenaKit.ArenaId), DeleteBehavior.Cascade },
        new object[] { typeof(ArenaKit), nameof(ArenaKit.KitId), DeleteBehavior.Restrict },
        new object[] { typeof(Arena), nameof(Arena.LobbyLocationId), DeleteBehavior.Restrict },
        new object[] { typeof(Arena), nameof(Arena.ExitLocationId), DeleteBehavior.Restrict },
        new object[] { typeof(Arena), nameof(Arena.StakeMinTitleBracketId), DeleteBehavior.SetNull },
        new object[] { typeof(ArenaBattleground), nameof(ArenaBattleground.SpawnALocationId), DeleteBehavior.Restrict },
        new object[] { typeof(ArenaBattleground), nameof(ArenaBattleground.SpawnBLocationId), DeleteBehavior.Restrict },
    };

    [Theory]
    [MemberData(nameof(ExpectedDeleteRules))]
    public void DeleteRule(Type entity, string foreignKey, DeleteBehavior expected)
    {
        var fk = _context.Model.FindEntityType(entity)!.GetForeignKeys()
            .Single(f => f.Properties.Any(p => p.Name == foreignKey));
        Assert.Equal(expected, fk.DeleteBehavior);
    }

    [Fact]
    public void TablesAndKeys_FollowTheDesign()
    {
        Assert.Equal("arenas", _context.Model.FindEntityType(typeof(Arena))!.GetTableName());
        Assert.Equal("arena_battlegrounds", _context.Model.FindEntityType(typeof(ArenaBattleground))!.GetTableName());
        Assert.Equal("arena_spectator_areas", _context.Model.FindEntityType(typeof(ArenaSpectatorArea))!.GetTableName());
        var kit = _context.Model.FindEntityType(typeof(ArenaKit))!;
        Assert.Equal("arena_kits", kit.GetTableName());
        Assert.Equal(new[] { "ArenaId", "KitId" }, kit.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Contains(_context.Model.FindEntityType(typeof(Arena))!.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == "Key");
    }

    // ===== Discovery and navigation types =====

    [Fact]
    public async Task DiscoveryNodes_TypeArenasAndHangChildrenUnderTheirArena()
    {
        var arena = await _service.CreateAsync(NewArena());
        var bg = await _service.CreateBattlegroundAsync(NewBattleground(arena.Id!.Value, "bg1"));
        var stands = await _service.CreateSpectatorAreaAsync(new ArenaSpectatorAreaDto { ArenaId = arena.Id, Name = "Stands", WgRegionId = "stands" });

        var nodes = (await new DiscoveryRepository(_context).GetAllDomainNodesAsync()).ToDictionary(n => n.Id);

        Assert.Equal(DiscoveryRewardRule.Arena, nodes[arena.Id.Value].DomainType);
        Assert.Equal(10, nodes[arena.Id.Value].ParentId);
        Assert.Equal((DiscoveryRewardRule.ArenaBattleground, (int?)arena.Id), (nodes[bg.Id!.Value].DomainType, nodes[bg.Id.Value].ParentId));
        Assert.Equal((DiscoveryRewardRule.ArenaSpectatorArea, (int?)arena.Id), (nodes[stands.Id!.Value].DomainType, nodes[stands.Id.Value].ParentId));
    }

    [Fact]
    public void Battlegrounds_AreNotANavigableType()
    {
        Assert.Contains(DomainNavigationDefault.Arena, DomainNavigationDefault.DomainTypes);
        Assert.Contains(DomainNavigationDefault.ArenaSpectatorArea, DomainNavigationDefault.DomainTypes);
        Assert.DoesNotContain(nameof(ArenaBattleground), DomainNavigationDefault.DomainTypes);
    }

    // ===== arena_2 seed =====

    [Fact]
    public async Task LegacySeed_CreatesTheArenaAndItsBattlegrounds_DisabledUntilLocationsAreSet_AndIsIdempotent()
    {
        var regions = Enumerable.Range(1, 8).Select(n => $"arena_2,{n}-battleground").ToList();
        var seed = new ArenaLegacySeedDto { DistrictId = 10, StreetId = 5, WorldName = "world", BattlegroundRegionIds = regions };

        var first = await _service.SeedLegacyArenaAsync(seed);

        Assert.True(first.ArenaCreated);
        Assert.Equal(8, first.BattlegroundsCreated.Count);
        var arena = await _context.Arenas.AsNoTracking().SingleAsync();
        Assert.Equal(("arena_2", "arena_2", false), (arena.Key, arena.WgRegionId, arena.IsDuelEnabled));
        var bgs = await _context.ArenaBattlegrounds.AsNoTracking().OrderBy(b => b.SortOrder).ToListAsync();
        Assert.Equal(regions, bgs.Select(b => b.WgRegionId));
        Assert.All(bgs, b => Assert.False(b.IsEnabled || b.AllowEntry));
        Assert.Equal(Enumerable.Range(1, 8).Select(n => $"bg{n}"), bgs.Select(b => b.Key));

        var again = await _service.SeedLegacyArenaAsync(seed);
        Assert.False(again.ArenaCreated);
        Assert.Empty(again.BattlegroundsCreated);
        Assert.Equal(8, again.BattlegroundsExisting.Count);
        Assert.Equal(8, await _context.ArenaBattlegrounds.CountAsync());
    }

    [Fact]
    public async Task LegacySeed_NeedsTheBattlegroundRegions()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SeedLegacyArenaAsync(new ArenaLegacySeedDto { DistrictId = 10, StreetId = 5 }));
    }
}
