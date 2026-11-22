using AutoMapper;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Landmarks as a Domain subtype, phase 2 (PandiO/knk-workspace#984, DESIGN § 5 rules 6, 9, 10, § 6.2 items 2–3,
/// LD15, LD20–LD22): the domain search lists landmarks with the type <c>Landmark</c> and filters on the type (A07);
/// discovery has a disabled <c>Landmark</c> rule and only a landmark with its own region and its override on is
/// discoverable (A08); a landmark with its warp on is a teleport destination of type <c>Landmark</c> with its price,
/// and "requires discovery" unlocks once it was discovered (A09, API side). In-memory database, real services.
/// </summary>
public class LandmarkDomainIntegrationTests : IDisposable
{
    private const int UserId = 100;
    private const int Rivia = 1, Smithy = 2, NorthGate = 3, Well = 10, Tower = 11, Palace = 12, ThroneRoom = 13, Dais = 14,
        Square = 15;

    private readonly KnKDbContext _db;

    public LandmarkDomainIntegrationTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase("landmark-integration-" + Guid.NewGuid())
            .Options);

        _db.TitleBrackets.AddRange(DiscoveryRewardCalculatorTests.RealBrackets(idOffset: 1));
        _db.DiscoveryRewardRules.AddRange(
            DiscoveryRewardCalculatorTests.TownRule(),
            DiscoveryRewardCalculatorTests.StructureRule(),
            DiscoveryRewardCalculatorTests.StructureRule("GateStructure"),
            // As the AddLandmarkDiscoveryRule migration seeds it.
            new DiscoveryRewardRule
            {
                DomainType = "Landmark", IsEnabled = false, ExpUnitsMin = 0.05m, ExpUnitsMax = 0.25m,
                CoinSalaryHoursMin = 0.05m, CoinSalaryHoursMax = 0.25m, GemsMin = 0, GemsMax = 0, IncludeAncestors = false
            });

        _db.Streets.Add(new Street { Id = 1, Name = "Main" });
        _db.Towns.Add(new Town { Id = Rivia, Name = "Rivia", Description = "", WgRegionId = "town_rivia", WorldName = "world",
            Location = new Location { Name = "Rivia spawn", World = "world", X = 0, Y = 64, Z = 0 } });
        _db.Structures.Add(new Structure { Id = Smithy, Name = "Smithy", Description = "", WgRegionId = "structure_smithy", StreetId = 1, WorldName = "world" });
        _db.GateStructures.Add(new GateStructure { Id = NorthGate, Name = "North Gate", Description = "", WgRegionId = "gate_north", StreetId = 1, WorldName = "world" });

        _db.Landmarks.AddRange(
            Landmark(Well, "Well", LandmarkAreaKind.None, null),
            Landmark(Tower, "Old Tower", LandmarkAreaKind.OwnRegion, "domain_11"),
            Landmark(Palace, "Palace", LandmarkAreaKind.OwnRegion, "domain_12", parent: Rivia),
            Landmark(ThroneRoom, "Throne room", LandmarkAreaKind.OwnRegion, "domain_13", parent: Palace),
            Landmark(Dais, "Dais", LandmarkAreaKind.None, null, parent: ThroneRoom),
            Landmark(Square, "Square", LandmarkAreaKind.LinkedDomain, null, parent: Rivia));

        _db.Users.Add(new User { Id = UserId, Username = "alice", Uuid = "uuid-a", Coins = 250, Gems = 50, ExperiencePoints = 0 });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose() => _db.Dispose();

    private static Landmark Landmark(int id, string name, LandmarkAreaKind area, string? region, int? parent = null) => new()
    {
        Id = id, Name = name, Description = "", AreaKind = area, WgRegionId = region, ParentDomainId = parent, WorldName = "world",
        Location = new Location { Name = name, World = "world", X = id, Y = 64, Z = -id },
    };

    // ===== A07: domain search with the type and the type filter (LD20) =====

    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<DomainMappingProfile>();
        cfg.AddProfile<PagedQueryMappingProfile>();
    }).CreateMapper();

    private DomainService Domains() => new(new DomainRepository(_db), Mapper(),
        new Mock<IDomainRegionNameFinalizer>().Object, NullLogger<DomainService>.Instance, FixedWorldResolver.Instance);

    private Task<PagedResultDto<DomainListDto>> SearchAsync(params (string Key, string Value)[] filters) =>
        Domains().SearchAsync(new PagedQueryDto
        {
            PageNumber = 1, PageSize = 50, SortBy = "id",
            Filters = filters.Length == 0 ? null : filters.ToDictionary(f => f.Key, f => f.Value)
        });

    private static int[] Ids(PagedResultDto<DomainListDto> page) => page.Items.Select(i => i.Id!.Value).ToArray();

    [Fact]
    public async Task Search_ListsLandmarks_WithTheTypeLandmark()
    {
        var page = await SearchAsync();

        Assert.Equal(9, page.TotalCount);
        var landmarks = page.Items.Where(i => i.DomainType == "Landmark").Select(i => i.Id!.Value).ToArray();
        Assert.Equal(new[] { Well, Tower, Palace, ThroneRoom, Dais, Square }, landmarks);
        Assert.Equal("Town", page.Items.Single(i => i.Id == Rivia).DomainType);
    }

    [Fact]
    public async Task Search_TheTypeFilter_KeepsOnlyThatType_AndCountsOnlyThose()
    {
        var page = await SearchAsync((DomainRepository.DomainTypeFilter, "landmark"));

        Assert.Equal(new[] { Well, Tower, Palace, ThroneRoom, Dais, Square }, Ids(page));
        Assert.Equal(6, page.TotalCount);
        Assert.All(page.Items, i => Assert.Equal("Landmark", i.DomainType));
    }

    [Fact]
    public async Task Search_TheTypeFilter_IsTheExactType_TakesSeveral_AndAnUnknownTypeMatchesNothing()
    {
        // Structure means plain structures only, as the type column shows a gate as GateStructure.
        Assert.Equal(new[] { Smithy }, Ids(await SearchAsync((DomainRepository.DomainTypeFilter, "Structure"))));
        Assert.Equal(new[] { Rivia, NorthGate },
            Ids(await SearchAsync((DomainRepository.DomainTypeFilter, "Town, GateStructure"))));

        var none = await SearchAsync((DomainRepository.DomainTypeFilter, "Castle"));
        Assert.Empty(none.Items);
        Assert.Equal(0, none.TotalCount);
    }

    [Fact]
    public async Task Search_ExcludeSubtreeOf_LeavesOutTheLandmarkAndItsDescendants()
    {
        var page = await SearchAsync((DomainRepository.ExcludeSubtreeFilter, Palace.ToString()));

        Assert.DoesNotContain(Palace, Ids(page));
        Assert.DoesNotContain(ThroneRoom, Ids(page));
        Assert.DoesNotContain(Dais, Ids(page));
        Assert.Equal(new[] { Rivia, Smithy, NorthGate, Well, Tower, Square }, Ids(page));

        // Combined with the type filter: a landmark's parent picker narrowed to landmarks.
        var landmarks = await SearchAsync((DomainRepository.DomainTypeFilter, "Landmark"),
            (DomainRepository.ExcludeSubtreeFilter, ThroneRoom.ToString()));
        Assert.Equal(new[] { Well, Tower, Palace, Square }, Ids(landmarks));

        // Not a number: ignored, like the other id filters.
        Assert.Equal(9, (await SearchAsync((DomainRepository.ExcludeSubtreeFilter, "x"))).TotalCount);
    }

    // ===== A08: the disabled Landmark rule and the per-domain override (LD15) =====

    private DiscoveryService Discovery()
    {
        var userRepo = new UserRepository(_db);
        var groupRepo = new PermissionGroupRepository(_db);
        var audit = new AuditLogService(new AuditLogRepository(_db), userRepo);
        var titles = new TitleService(new TitleBracketRepository(_db));
        var memberships = new UserPermissionGroupService(new UserPermissionGroupRepository(_db), userRepo, groupRepo, audit);
        var currency = new CurrencyService(new CurrencyRepository(_db), userRepo, NullLogger<CurrencyService>.Instance);
        var titleProgression = new TitleProgressionService(currency, userRepo, titles, memberships, audit);
        var users = new UserService(userRepo, new Mock<IMapper>().Object, new Mock<IPasswordService>().Object,
            new Mock<ILinkCodeService>().Object, titles, memberships, audit, groupRepo,
            NullLogger<UserService>.Instance, currency, titleProgression, new Mock<IPlayerNotificationQueue>().Object);
        return new DiscoveryService(new DiscoveryRepository(_db), userRepo, users, currency, titles, memberships, audit,
            NullLogger<DiscoveryService>.Instance, Options.Create(new DiscoveryOptions { MaxNewPerHour = 120 }),
            new DiscoveryRewardCalculatorTests.FixedRandom(0.5), new InMemoryPlayerNotificationQueue());
    }

    private void Override(int domainId, bool enabled)
    {
        _db.DomainDiscoveryOverrides.Add(new DomainDiscoveryOverride { DomainId = domainId, IsEnabled = enabled });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private static DiscoveryGrantRequestDto Regions(params string[] ids) =>
        new() { WgRegionIds = ids.ToList(), Source = "RegionEnter", WorldName = "world" };

    [Fact]
    public async Task TheLandmarkRule_IsListedAndDisabled_AndCanBeEdited()
    {
        var config = new DiscoveryConfigurationService(new DiscoveryRepository(_db), new Mock<ITitleService>().Object);

        var rule = (await config.GetRulesAsync()).Single(r => r.DomainType == "Landmark");
        Assert.False(rule.IsEnabled);

        var updated = await config.UpdateRuleAsync("landmark", new UpdateDiscoveryRewardRuleDto
        {
            IsEnabled = false, ExpUnitsMin = 0.1m, ExpUnitsMax = 0.5m, CoinSalaryHoursMin = 0, CoinSalaryHoursMax = 0,
            GemsMin = 0, GemsMax = 1
        });
        Assert.Equal(("Landmark", 0.5m), (updated.DomainType, updated.ExpUnitsMax));
    }

    [Fact]
    public async Task ALandmarkWithItsOverrideOn_IsDiscovered_OneWithout_IsNot()
    {
        Override(Tower, enabled: true);

        var result = await Discovery().DiscoverAsync(UserId, Regions("domain_11", "domain_12"));

        var grant = Assert.Single(result.Granted);
        Assert.Equal((Tower, "Landmark"), (grant.DomainId, grant.DomainType));
        Assert.Contains(result.Skipped, s => s.Key == "domain_12" && s.Reason == DiscoverySkipDto.Disabled);
        Assert.True(_db.UserDomainDiscoveries.Any(d => d.UserId == UserId && d.DomainId == Tower));
        Assert.False(_db.UserDomainDiscoveries.Any(d => d.UserId == UserId && d.DomainId == Palace));
    }

    [Fact]
    public async Task ALandmarkInALandmark_IsDiscoveredAsItself_WithoutItsParents()
    {
        Override(ThroneRoom, enabled: true);
        Override(Palace, enabled: true);

        var result = await Discovery().DiscoverAsync(UserId, Regions("domain_13"));

        // The Landmark rule includes no ancestors: the palace and the town are discovered by entering them.
        Assert.Equal(new[] { ThroneRoom }, result.Granted.Select(g => g.DomainId).ToArray());
    }

    [Fact]
    public async Task OnlyALandmarkWithItsOwnRegion_IsADiscoveryNode()
    {
        Override(Well, enabled: true);
        Override(Square, enabled: true);
        Override(Tower, enabled: true);

        var enabled = await DiscoveryEnabledDomains.LoadAsync(new DiscoveryRepository(_db));

        // The point and the landmark that uses its parent's area (DESIGN § 5 rule 6) are no nodes, even with an override.
        Assert.Contains(Tower, enabled.Keys);
        Assert.DoesNotContain(Well, enabled.Keys);
        Assert.DoesNotContain(Square, enabled.Keys);
        // Without an override the Landmark rule keeps the others out.
        Assert.DoesNotContain(Palace, enabled.Keys);

        var byId = await Discovery().DiscoverAsync(UserId, new DiscoveryGrantRequestDto { DomainIds = new() { Square }, Source = "RegionEnter" });
        Assert.Empty(byId.Granted);
        Assert.Contains(byId.Skipped, s => s.Key == Square.ToString() && s.Reason == DiscoverySkipDto.NotADomain);
    }

    // ===== A09: warps with the type Landmark and "requires discovery" (LD21) =====

    private TeleportDestinationService Teleports()
    {
        var users = new UserRepository(_db);
        var groups = new UserPermissionGroupService(new UserPermissionGroupRepository(_db), users,
            new PermissionGroupRepository(_db), new Mock<IAuditLogService>().Object);
        return new TeleportDestinationService(new TeleportDestinationRepository(_db), users, groups,
            new DiscoveryRepository(_db), new TitleBracketRepository(_db), new PermissionGroupRepository(_db),
            new CurrencyService(new CurrencyRepository(_db), users, NullLogger<CurrencyService>.Instance),
            new CurrencyRepository(_db), NullLogger<TeleportDestinationService>.Instance);
    }

    private void Warp(int domainId, int priceGems, bool requiresDiscovery = false)
    {
        var domain = _db.Domains.Single(d => d.Id == domainId);
        domain.TeleportEnabled = true;
        domain.TeleportPriceGems = priceGems;
        domain.TeleportRequiresDiscovery = requiresDiscovery;
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ALandmarkWithItsWarpOn_IsADestinationOfTypeLandmark_WithItsPrice_AfterTheOtherTypes()
    {
        Warp(Tower, priceGems: 1);
        Warp(Rivia, priceGems: 0);

        var destinations = await Teleports().ListForUserAsync(UserId);

        Assert.Equal(new[] { (Rivia, "Town"), (Tower, "Landmark") }, destinations.Select(d => (d.DomainId, d.DomainType)).ToArray());
        var tower = destinations[1];
        Assert.Equal(("Old Tower", 1, true), (tower.Name, tower.PriceGems, tower.Available));
        // The landmark without its warp (TeleportEnabled off by default, LD24) is not listed.
        Assert.DoesNotContain(destinations, d => d.DomainId == Palace);
    }

    [Fact]
    public async Task RequiresDiscovery_StaysLockedWhileDiscoveryIsOff_AndUnlocksAfterTheDiscovery()
    {
        Warp(Tower, priceGems: 1, requiresDiscovery: true);

        // Discovery off (the Landmark rule is disabled and there is no override): entering the region discovers
        // nothing, so the warp stays locked.
        Assert.Empty((await Discovery().DiscoverAsync(UserId, Regions("domain_11"))).Granted);
        var locked = Assert.Single(await Teleports().ListForUserAsync(UserId));
        Assert.Equal((false, TeleportDestinationException.NotDiscovered), (locked.Available, locked.LockCode));

        Override(Tower, enabled: true);
        Assert.Single((await Discovery().DiscoverAsync(UserId, Regions("domain_11"))).Granted);
        _db.ChangeTracker.Clear();

        var open = Assert.Single(await Teleports().ListForUserAsync(UserId));
        Assert.Equal(("Landmark", true, true), (open.DomainType, open.RequiresDiscovery, open.Available));
    }
}
