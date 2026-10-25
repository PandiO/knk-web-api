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
/// Arena duels Phase 1 (KNG-210, docs/specs/arena-duels/DESIGN.md §4.1–4.2) and Phase 9 (rev. 6): arena CRUD with the
/// design defaults, the settings validation, eligible kits in order, the arena areas (battleground, spectator area,
/// lobby) and their regions with their validation, the region names on save, the model's delete rules, and no
/// discovery or navigation for areas.
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
            new DomainWorldResolver(_context),
            _context);
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
            StreetId = 5
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
        // Rev. 6: a new arena starts closed (its lobby is an area added afterwards); the model default stays true.
        Assert.False(arena.IsDuelEnabled);
        Assert.True(new Arena().IsDuelEnabled);
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
    public async Task GetByKey_IsCaseInsensitive_AndCarriesTheAreasWithTheirRegions()
    {
        var created = await _service.CreateAsync(NewArena());
        var bg = await _service.CreateAreaAsync(NewArea(created.Id!.Value, ArenaAreaKind.BATTLEGROUND, "bg1"));
        var region = await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = bg.Id, WgRegionId = "pit_a" });
        await _service.CreateAreaAsync(NewArea(created.Id.Value, ArenaAreaKind.SPECTATOR, "stands"));

        var read = await _service.GetByKeyAsync("ARENA_2");

        Assert.NotNull(read);
        Assert.Equal(new[] { "BATTLEGROUND", "SPECTATOR" }, read!.Areas!.Select(a => a.Kind));
        // Adopted with "select" and renamed on save (KNG-253 Q-select).
        Assert.Equal($"arena_{created.Id}_battleground_{region.Id}", Assert.Single(read.Areas![0].Regions!).WgRegionId);
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
    public async Task Delete_RemovesTheArenaWithItsAreasAndRegions()
    {
        var created = await _service.CreateAsync(NewArena(d => { d.AllowFixedKit = true; d.EligibleKits = new() { new ArenaKitDto { KitId = 7 } }; }));
        var bg = await _service.CreateAreaAsync(NewArea(created.Id!.Value, ArenaAreaKind.BATTLEGROUND, "bg1"));
        await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = bg.Id, WgRegionId = "pit_a" });

        await _service.DeleteAsync(created.Id.Value);

        Assert.Empty(_context.Arenas);
        Assert.Empty(_context.ArenaKits);
        Assert.Empty(_context.ArenaAreas);
        Assert.Empty(_context.ArenaAreaRegions);
        Assert.False(await _context.Domains.AnyAsync(d => d.Id == created.Id));
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
    public async Task DuelsEnabled_NeedAnEnabledLobbyAreaWithAnArrival()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.IsDuelEnabled = true)));
        Assert.Contains("lobby", ex.Message);
        var arena = await _service.CreateAsync(NewArena());

        // A lobby without an arrival, or disabled, doesn't count.
        var lobby = await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.LOBBY, "lobby", d => d.ArrivalLocationId = null));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(arena.Id.Value, NewArena(d => d.IsDuelEnabled = true)));
        await _service.UpdateAreaAsync(lobby.Id!.Value, NewArea(arena.Id.Value, ArenaAreaKind.LOBBY, "lobby", d => d.IsEnabled = false));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(arena.Id.Value, NewArena(d => d.IsDuelEnabled = true)));

        await _service.UpdateAreaAsync(lobby.Id.Value, NewArea(arena.Id.Value, ArenaAreaKind.LOBBY, "lobby"));
        await _service.UpdateAsync(arena.Id.Value, NewArena(d => d.IsDuelEnabled = true));
        Assert.True((await _context.Arenas.AsNoTracking().SingleAsync()).IsDuelEnabled);

        // While duels are on, the lobby stays usable: not disabled, emptied, turned into another kind or deleted.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAreaAsync(lobby.Id.Value, NewArea(arena.Id.Value, ArenaAreaKind.LOBBY, "lobby", d => d.IsEnabled = false)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAreaAsync(lobby.Id.Value, NewArea(arena.Id.Value, ArenaAreaKind.LOBBY, "lobby", d => d.ArrivalLocationId = null)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAreaAsync(lobby.Id.Value, NewArea(arena.Id.Value, ArenaAreaKind.SPECTATOR, "lobby")));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeleteAreaAsync(lobby.Id.Value));

        await _service.UpdateAsync(arena.Id.Value, NewArena(d => d.IsDuelEnabled = false));
        await _service.DeleteAreaAsync(lobby.Id.Value);
        Assert.Empty(_context.ArenaAreas);
    }

    [Fact]
    public async Task AnExitInAnotherWorld_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(NewArena(d => d.ExitLocationId = 104)));
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

    // ===== Areas and their regions (rev. 6) =====

    private static ArenaAreaDto NewArea(int arenaId, ArenaAreaKind kind, string key, Action<ArenaAreaDto>? change = null)
    {
        var dto = new ArenaAreaDto
        {
            ArenaId = arenaId,
            Kind = kind.ToString(),
            Key = key,
            Name = "Area " + key,
            IsEnabled = kind != ArenaAreaKind.BATTLEGROUND,
            SpawnALocationId = 102,
            SpawnBLocationId = 103,
            ArrivalLocationId = 100,
            TeleportMinTitleBracketId = 3,
            TeleportPriceGems = 10
        };
        change?.Invoke(dto);
        return dto;
    }

    [Fact]
    public async Task AnArea_KeepsOnlyTheFieldsOfItsKind()
    {
        var arena = await _service.CreateAsync(NewArena());

        var bg = await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.BATTLEGROUND, "bg1"));
        var stands = await _service.CreateAreaAsync(NewArea(arena.Id.Value, ArenaAreaKind.SPECTATOR, "stands"));
        var lobby = await _service.CreateAreaAsync(NewArea(arena.Id.Value, ArenaAreaKind.LOBBY, "lobby"));

        Assert.Equal((102, 103, (int?)null, (int?)null, (int?)null), (bg.SpawnALocationId!.Value, bg.SpawnBLocationId!.Value, bg.ArrivalLocationId, bg.TeleportMinTitleBracketId, bg.TeleportPriceGems));
        Assert.Equal(((int?)null, (int?)null, 100, 3, 10), (stands.SpawnALocationId, stands.SpawnBLocationId, stands.ArrivalLocationId!.Value, stands.TeleportMinTitleBracketId!.Value, stands.TeleportPriceGems!.Value));
        Assert.Equal("Peasant", stands.TeleportMinTitleBracketName);
        Assert.Equal(((int?)null, (int?)null, 100, (int?)null, (int?)null), (lobby.SpawnALocationId, lobby.SpawnBLocationId, lobby.ArrivalLocationId!.Value, lobby.TeleportMinTitleBracketId, lobby.TeleportPriceGems));
        Assert.Equal("Arena", bg.ArenaName);
        Assert.Equal("arena_2", bg.ArenaWgRegionId);
        Assert.False(new ArenaArea().IsEnabled);
    }

    [Fact]
    public async Task AnArea_NeedsAKindAKeyUniqueInTheArena_AndAnExistingArena()
    {
        var arena = await _service.CreateAsync(NewArena());
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.SPECTATOR, "a", d => d.Kind = null)));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.SPECTATOR, "a", d => d.Kind = "ARENA")));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAreaAsync(NewArea(999, ArenaAreaKind.SPECTATOR, "a")));
        await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.SPECTATOR, "a"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAreaAsync(NewArea(arena.Id.Value, ArenaAreaKind.BATTLEGROUND, "A")));
    }

    [Fact]
    public async Task AnArenaHasAtMostOneLobby()
    {
        var arena = await _service.CreateAsync(NewArena());
        await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.LOBBY, "lobby"));
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAreaAsync(NewArea(arena.Id.Value, ArenaAreaKind.LOBBY, "lobby2")));
        Assert.Contains("lobby", ex.Message);
    }

    [Fact]
    public async Task AnEnabledBattleground_NeedsBothSpawnsAndARegion()
    {
        var arena = await _service.CreateAsync(NewArena());
        // No region yet: only disabled.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.BATTLEGROUND, "bg1", d => d.IsEnabled = true)));
        Assert.Contains("region", ex.Message);
        var bg = await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.BATTLEGROUND, "bg1"));
        await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = bg.Id, WgRegionId = "pit_a" });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAreaAsync(bg.Id!.Value, NewArea(arena.Id.Value, ArenaAreaKind.BATTLEGROUND, "bg1", d => { d.IsEnabled = true; d.SpawnBLocationId = null; })));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAreaAsync(bg.Id!.Value, NewArea(arena.Id.Value, ArenaAreaKind.BATTLEGROUND, "bg1", d => { d.IsEnabled = true; d.SpawnBLocationId = 102; })));
        await _service.UpdateAreaAsync(bg.Id!.Value, NewArea(arena.Id.Value, ArenaAreaKind.BATTLEGROUND, "bg1", d => d.IsEnabled = true));
        Assert.True((await _context.ArenaAreas.AsNoTracking().SingleAsync()).IsEnabled);

        // Its last region can't go while it is enabled.
        var region = Assert.Single(await _service.GetAreaRegionsAsync(bg.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeleteAreaRegionAsync(region.Id!.Value));
        await _service.UpdateAreaAsync(bg.Id!.Value, NewArea(arena.Id.Value, ArenaAreaKind.BATTLEGROUND, "bg1", d => d.IsEnabled = false));
        await _service.DeleteAreaRegionAsync(region.Id!.Value);
        Assert.Empty(_context.ArenaAreaRegions);
    }

    [Fact]
    public async Task SpawnsAndArrivals_MustBeInTheWorldOfTheAreasRegions()
    {
        var arena = await _service.CreateAsync(NewArena());
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.BATTLEGROUND, "bg1", d => d.SpawnBLocationId = 104)));
        Assert.Contains("world", ex.Message);
        ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.SPECTATOR, "stands", d => d.ArrivalLocationId = 104)));
        Assert.Contains("world", ex.Message);
    }

    [Fact]
    public async Task AnAreasKind_CantChangeWhileItHasRegions()
    {
        var arena = await _service.CreateAsync(NewArena());
        var area = await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.SPECTATOR, "stands"));
        await _service.UpdateAreaAsync(area.Id!.Value, NewArea(arena.Id.Value, ArenaAreaKind.BATTLEGROUND, "stands", d => d.IsEnabled = false));
        await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = area.Id, WgRegionId = "stands_1" });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAreaAsync(area.Id.Value, NewArea(arena.Id.Value, ArenaAreaKind.SPECTATOR, "stands")));
    }

    [Fact]
    public async Task AnAreaRegion_IsInTheArenasWorld_AndUsedByNothingElse()
    {
        var arena = await _service.CreateAsync(NewArena());
        var bg = await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.BATTLEGROUND, "bg1"));
        var stands = await _service.CreateAreaAsync(NewArea(arena.Id.Value, ArenaAreaKind.SPECTATOR, "stands"));

        var region = await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = bg.Id, WgRegionId = "pit_a" });
        Assert.Equal(("world", (int?)arena.Id, "BATTLEGROUND", "bg1"), (region.WorldName, region.ArenaId, region.AreaKind, region.AreaKey));

        // Another area's region, the arena's own region, a domain's region, another world.
        string taken = region.WgRegionId;
        await Assert.ThrowsAsync<DomainWorldException>(() => _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = stands.Id, WgRegionId = taken.ToUpperInvariant() }));
        await Assert.ThrowsAsync<DomainWorldException>(() => _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = stands.Id, WgRegionId = "arena_2" }));
        await Assert.ThrowsAsync<DomainWorldException>(() => _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = stands.Id, WgRegionId = "domain_10" }));
        await Assert.ThrowsAsync<DomainWorldException>(() =>
            _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = stands.Id, WgRegionId = "ring", WorldName = "world_nether" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = 999, WgRegionId = "x" }));

        // And a domain can't take an area region's id either.
        var owner = await DomainRegionOwnership.TakenByAsync(_context, taken, "world");
        Assert.Contains("bg1", owner);

        // A region can't move to another area.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAreaRegionAsync(region.Id!.Value, new ArenaAreaRegionDto { ArenaAreaId = stands.Id, WgRegionId = taken }));
    }

    [Fact]
    public async Task AreaRegions_AreRenamedOnSave_WithTheirKindAndTheArenaAsParent()
    {
        var arena = await _service.CreateAsync(NewArena(d => d.WgRegionId = "tempregion_worldtask_1"));
        var bg = await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.BATTLEGROUND, "bg1"));
        var stands = await _service.CreateAreaAsync(NewArea(arena.Id.Value, ArenaAreaKind.SPECTATOR, "stands"));
        var lobby = await _service.CreateAreaAsync(NewArea(arena.Id.Value, ArenaAreaKind.LOBBY, "lobby"));

        var part1 = await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = bg.Id, WgRegionId = "tempregion_worldtask_2" });
        // An adopted v1 region is renamed too (KNG-253 Q-select).
        var part2 = await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = bg.Id, WgRegionId = "arena_2,a-battleground" });
        var ring = await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = stands.Id, WgRegionId = "tempregion_worldtask_3" });
        var hall = await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = lobby.Id, WgRegionId = "tempregion_worldtask_4" });

        string parent = $"domain_{arena.Id}";
        Assert.Equal(new[]
        {
            ("tempregion_worldtask_1", parent, (string?)"Arena", (string?)"domain_10"),
            ("tempregion_worldtask_2", $"arena_{arena.Id}_battleground_{part1.Id}", "ArenaBattleground", parent),
            ("arena_2,a-battleground", $"arena_{arena.Id}_battleground_{part2.Id}", "ArenaBattleground", parent),
            ("tempregion_worldtask_3", $"arena_{arena.Id}_spectator_{ring.Id}", "ArenaSpectatorArea", parent),
            ("tempregion_worldtask_4", $"arena_{arena.Id}_lobby_{hall.Id}", "ArenaLobby", parent)
        }, _renames);
        Assert.Equal($"arena_{arena.Id}_battleground_{part1.Id}", part1.WgRegionId);
        Assert.Equal($"arena_{arena.Id}_lobby_{hall.Id}", (await _context.ArenaAreaRegions.AsNoTracking().SingleAsync(r => r.Id == hall.Id)).WgRegionId);

        // A failed rename keeps the drawn name (the finalize pass retries it).
        _regions.Setup(r => r.RenameRegionAsync("tempregion_worldtask_5", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(false);
        var kept = await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = stands.Id, WgRegionId = "tempregion_worldtask_5" });
        Assert.Equal("tempregion_worldtask_5", kept.WgRegionId);
    }

    [Fact]
    public async Task ARerunOfARegionsWorldTask_ReplacesItsOwnFinalRegion()
    {
        var replaced = new List<(string Old, string New, string? Replace)>();
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?, string?, string?>((o, n, _, _, _, rep) => replaced.Add((o, n, rep)))
            .ReturnsAsync(true);
        var arena = await _service.CreateAsync(NewArena());
        var bg = await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.BATTLEGROUND, "bg1"));
        var part = await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = bg.Id, WgRegionId = "tempregion_worldtask_2" });
        string finalName = $"arena_{arena.Id}_battleground_{part.Id}";
        Assert.Equal(finalName, part.WgRegionId);

        await _service.UpdateAreaRegionAsync(part.Id!.Value, new ArenaAreaRegionDto { WgRegionId = "tempregion_worldtask_9" });

        Assert.Equal(("tempregion_worldtask_9", finalName, (string?)finalName), Assert.Single(replaced));
    }

    [Fact]
    public async Task FinalizeAll_RenamesAreaRegionsLeftOnATempName()
    {
        _regions.Setup(r => r.RenameRegionAsync("tempregion_worldtask_7", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(false);
        var arena = await _service.CreateAsync(NewArena());
        var stands = await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.SPECTATOR, "stands"));
        var ring = await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = stands.Id, WgRegionId = "tempregion_worldtask_7" });
        _regions.Setup(r => r.RenameRegionAsync("tempregion_worldtask_7", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        var result = await new DomainRegionNameFinalizer(_context, _regions.Object, NullLogger<DomainRegionNameFinalizer>.Instance).FinalizeAllAsync();

        Assert.Equal(1, result.Renamed);
        Assert.Equal($"arena_{arena.Id}_spectator_{ring.Id}", (await _context.ArenaAreaRegions.AsNoTracking().SingleAsync()).WgRegionId);
    }

    [Fact]
    public async Task ByRegion_FindsTheAreaRegion_ForTheOrphanCheck()
    {
        var arena = await _service.CreateAsync(NewArena());
        var bg = await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.BATTLEGROUND, "bg1"));
        var part = await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = bg.Id, WgRegionId = "pit_a" });

        string name = part.WgRegionId;
        Assert.Equal(part.Id, (await _service.FindAreaRegionAsync(name.ToUpperInvariant(), "world"))!.Id);
        Assert.Equal(part.Id, (await _service.FindAreaRegionAsync(name, null))!.Id);
        Assert.Null(await _service.FindAreaRegionAsync(name, "world_nether"));
        Assert.Null(await _service.FindAreaRegionAsync("pit_a", "world"));
    }

    [Fact]
    public async Task DeletingAnArea_RemovesItsRegions()
    {
        var arena = await _service.CreateAsync(NewArena());
        var bg = await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.BATTLEGROUND, "bg1"));
        await _service.CreateAreaRegionAsync(new ArenaAreaRegionDto { ArenaAreaId = bg.Id, WgRegionId = "pit_a" });

        await _service.DeleteAreaAsync(bg.Id!.Value);

        Assert.Empty(_context.ArenaAreas);
        Assert.Empty(_context.ArenaAreaRegions);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteAreaAsync(bg.Id.Value));
    }

    [Fact]
    public async Task TheArenaReadCarriesItsLocationsForThePlugin()
    {
        var created = await _service.CreateAsync(NewArena(d => { d.LocationId = 101; d.ExitLocationId = 101; }));
        await _service.CreateAreaAsync(NewArea(created.Id!.Value, ArenaAreaKind.BATTLEGROUND, "bg1"));
        await _service.CreateAreaAsync(NewArea(created.Id.Value, ArenaAreaKind.LOBBY, "lobby"));

        var read = Assert.Single(await _service.GetAllAsync());

        Assert.Equal("exit", read.Location!.Name);
        Assert.Equal("exit", read.ExitLocation!.Name);
        var bg = read.Areas!.Single(a => a.Kind == "BATTLEGROUND");
        Assert.Equal("spawn A", bg.SpawnALocation!.Name);
        Assert.Equal("spawn B", bg.SpawnBLocation!.Name);
        Assert.Equal("lobby", read.Areas!.Single(a => a.Kind == "LOBBY").ArrivalLocation!.Name);
        Assert.Empty(read.SubRegions!);
    }

    [Fact]
    public async Task ADuelMatch_StartsOnlyOnABattlegroundAreaOfItsArena()
    {
        var arena = await _service.CreateAsync(NewArena());
        var stands = await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.SPECTATOR, "stands"));
        Assert.Equal(ArenaAreaKind.SPECTATOR, (await _context.ArenaAreas.AsNoTracking().SingleAsync(a => a.Id == stands.Id)).Kind);
        Assert.Equal("ArenaLobby", DomainRegionNameFinalizer.AreaRegionTypeOf(ArenaAreaKind.LOBBY));
        Assert.Equal("arena_4_spectator_9", DomainRegionNameFinalizer.FinalAreaRegionNameFor(4, ArenaAreaKind.SPECTATOR, 9));
    }

    // ===== Region finalizer: District → Arena =====

    [Fact]
    public async Task TheArenasTempRegion_IsRenamedWithItsTypeAndParent()
    {
        var arena = await _service.CreateAsync(NewArena(d => d.WgRegionId = "tempregion_worldtask_1"));

        Assert.Equal(new[] { ("tempregion_worldtask_1", $"domain_{arena.Id}", (string?)"Arena", (string?)"domain_10") }, _renames);
    }

    [Fact]
    public void Finalizer_NamesTheArenaType()
    {
        Assert.Equal("Arena", DomainRegionNameFinalizer.DomainTypeOf(new Arena()));
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
        new object[] { typeof(ArenaArea), nameof(ArenaArea.ArenaId), DeleteBehavior.Cascade },
        new object[] { typeof(ArenaAreaRegion), nameof(ArenaAreaRegion.ArenaAreaId), DeleteBehavior.Cascade },
        new object[] { typeof(ArenaKit), nameof(ArenaKit.ArenaId), DeleteBehavior.Cascade },
        new object[] { typeof(ArenaKit), nameof(ArenaKit.KitId), DeleteBehavior.Restrict },
        new object[] { typeof(Arena), nameof(Arena.ExitLocationId), DeleteBehavior.Restrict },
        new object[] { typeof(Arena), nameof(Arena.StakeMinTitleBracketId), DeleteBehavior.SetNull },
        new object[] { typeof(ArenaArea), nameof(ArenaArea.SpawnALocationId), DeleteBehavior.Restrict },
        new object[] { typeof(ArenaArea), nameof(ArenaArea.SpawnBLocationId), DeleteBehavior.Restrict },
        new object[] { typeof(ArenaArea), nameof(ArenaArea.ArrivalLocationId), DeleteBehavior.Restrict },
        new object[] { typeof(ArenaArea), nameof(ArenaArea.TeleportMinTitleBracketId), DeleteBehavior.SetNull },
        new object[] { typeof(DuelMatch), nameof(DuelMatch.BattlegroundId), DeleteBehavior.SetNull },
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
        Assert.Equal("arena_areas", _context.Model.FindEntityType(typeof(ArenaArea))!.GetTableName());
        Assert.Equal("arena_area_regions", _context.Model.FindEntityType(typeof(ArenaAreaRegion))!.GetTableName());
        // Rev. 6: areas are plain rows, not Domain subtypes.
        Assert.Null(_context.Model.FindEntityType(typeof(ArenaArea))!.BaseType);
        var kit = _context.Model.FindEntityType(typeof(ArenaKit))!;
        Assert.Equal("arena_kits", kit.GetTableName());
        Assert.Equal(new[] { "ArenaId", "KitId" }, kit.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Contains(_context.Model.FindEntityType(typeof(Arena))!.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == "Key");
    }

    // ===== Discovery and navigation types =====

    [Fact]
    public async Task DiscoveryNodes_TypeTheArena_AndAreasAreNoDomains()
    {
        var arena = await _service.CreateAsync(NewArena());
        await _service.CreateAreaAsync(NewArea(arena.Id!.Value, ArenaAreaKind.SPECTATOR, "stands"));

        var nodes = (await new DiscoveryRepository(_context).GetAllDomainNodesAsync()).ToDictionary(n => n.Id);

        Assert.Equal(DiscoveryRewardRule.Arena, nodes[arena.Id.Value].DomainType);
        Assert.Equal(10, nodes[arena.Id.Value].ParentId);
        Assert.Equal(new[] { "Town", "District", "Structure", "GateStructure", "Arena" }, DiscoveryRewardRule.DomainTypes);
    }

    [Fact]
    public void Areas_AreNotANavigableType()
    {
        Assert.Contains(DomainNavigationDefault.Arena, DomainNavigationDefault.DomainTypes);
        Assert.Equal(new[] { "Town", "District", "Structure", "GateStructure", "Arena" }, DomainNavigationDefault.DomainTypes);
    }
}
