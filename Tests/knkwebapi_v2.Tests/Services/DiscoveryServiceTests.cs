using System.Reflection;
using System.Text.Json;
using AutoMapper;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Domain discovery grants end to end on the in-memory database with the real UserService,
/// TitleService, rank multipliers and audit log (docs/specs/domain-discovery/DESIGN.md §3.4).
/// The row lock and transaction only exist on MySQL; they were exercised there by hand (ten
/// concurrent identical grants -> one set of rows, one credit, one audit row).
/// </summary>
public class DiscoveryServiceTests : IDisposable
{
    private const int UserId = 100;
    private const int Rivia = 1, OldQuarter = 2, Smithy = 3, NorthGate = 4, Kardenna = 5, Market = 6;

    private readonly KnKDbContext _db;
    private readonly Mock<IPlayerNotificationQueue> _notifications = new();

    public DiscoveryServiceTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        _db.TitleBrackets.AddRange(DiscoveryRewardCalculatorTests.RealBrackets(idOffset: 1));
        _db.DiscoveryRewardRules.AddRange(
            DiscoveryRewardCalculatorTests.TownRule(),
            DiscoveryRewardCalculatorTests.DistrictRule(),
            DiscoveryRewardCalculatorTests.StructureRule(),
            DiscoveryRewardCalculatorTests.StructureRule("GateStructure"));

        _db.Streets.Add(new Street { Id = 1, Name = "Main" });
        _db.Towns.Add(new Town { Id = Rivia, Name = "Rivia", Description = "", WgRegionId = "town_rivia" });
        _db.Towns.Add(new Town { Id = Kardenna, Name = "Kardenna", Description = "", WgRegionId = "town_kardenna" });
        _db.Districts.Add(new District { Id = OldQuarter, Name = "Old Quarter", Description = "", WgRegionId = "district_oldquarter", TownId = Rivia });
        _db.Districts.Add(new District { Id = Market, Name = "Market", Description = "", WgRegionId = "district_market", TownId = Kardenna });
        _db.Structures.Add(new Structure { Id = Smithy, Name = "Smithy", Description = "", WgRegionId = "structure_smithy", StreetId = 1, DistrictId = OldQuarter });
        _db.GateStructures.Add(new GateStructure { Id = NorthGate, Name = "North Gate", Description = "", WgRegionId = "gate_north", StreetId = 1, DistrictId = OldQuarter });

        _db.Users.Add(new User { Id = UserId, Username = "alice", Uuid = "uuid-a", Coins = 250, Gems = 50, ExperiencePoints = 0 });
        _db.Users.Add(new User { Id = 101, Username = "bob", Uuid = "uuid-b" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private DiscoveryService Service(int maxNewPerHour = 120, double roll = 0.5, IDiscoveryRepository? repo = null)
    {
        var userRepo = new UserRepository(_db);
        var groupRepo = new PermissionGroupRepository(_db);
        var audit = new AuditLogService(new AuditLogRepository(_db), userRepo);
        var titles = new TitleService(new TitleBracketRepository(_db));
        var memberships = new UserPermissionGroupService(new UserPermissionGroupRepository(_db), userRepo, groupRepo, audit);
        var users = new UserService(userRepo, new Mock<IMapper>().Object, new Mock<IPasswordService>().Object,
            new Mock<ILinkCodeService>().Object, titles, memberships, audit, groupRepo,
            NullLogger<UserService>.Instance, _notifications.Object);

        return new DiscoveryService(repo ?? new DiscoveryRepository(_db), userRepo, users, titles, memberships, audit,
            NullLogger<DiscoveryService>.Instance,
            Options.Create(new DiscoveryOptions { MaxNewPerHour = maxNewPerHour }),
            new DiscoveryRewardCalculatorTests.FixedRandom(roll));
    }

    private static DiscoveryGrantRequestDto Regions(params string[] ids) => new() { WgRegionIds = ids.ToList() };

    private User Alice() => _db.Users.AsNoTracking().Single(u => u.Id == UserId);

    private List<AuditLogEntry> Audit() => _db.AuditLogEntries.AsNoTracking().Where(a => a.TargetUserId == UserId).OrderBy(a => a.Id).ToList();

    [Fact]
    public async Task FirstGrant_WritesTheRowCreditsTheBalanceAndAuditsOnce()
    {
        var result = await Service().DiscoverAsync(UserId, new DiscoveryGrantRequestDto { WgRegionIds = new() { "town_kardenna" }, Source = "RegionEnter" });

        // Serf at roll 0.5: Town XP 2.5 units x 25 = 63, coins 5h x 650, gems 10.
        var grant = Assert.Single(result.Granted);
        Assert.Equal((Kardenna, "Kardenna", "Town", (string?)null, "RegionEnter", "town_kardenna"),
            (grant.DomainId, grant.Name, grant.DomainType, grant.ParentName, grant.Source, grant.WgRegionId));
        Assert.Equal((3250, 10, 63), (grant.Coins, grant.Gems, grant.Exp));
        Assert.Equal((3250, 10, 63), (result.TotalCoins, result.TotalGems, result.TotalExp));
        Assert.Equal((250 + 3250, 50 + 10, 63), (result.NewCoins, result.NewGems, result.NewExperiencePoints));
        Assert.Equal(1, result.TitleBracketId); // Serf (ids shifted by one, see RealBrackets)
        Assert.Null(result.TitleChange);

        var user = Alice();
        Assert.Equal((3500, 60, 63), (user.Coins, user.Gems, user.ExperiencePoints));

        var row = Assert.Single(_db.UserDomainDiscoveries.AsNoTracking());
        Assert.Equal((UserId, Kardenna, DiscoverySource.RegionEnter, 3250, 10, 63, (int?)1),
            (row.UserId, row.DomainId, row.Source, row.CoinsAwarded, row.GemsAwarded, row.ExpAwarded, row.TitleBracketId));

        var entry = Assert.Single(Audit());
        Assert.Equal(AuditAction.BalanceAdjusted, entry.Action);
        Assert.Null(entry.ActorUserId);
        using var details = JsonDocument.Parse(entry.Details!);
        Assert.Equal("domain-discovery", details.RootElement.GetProperty("reason").GetString());
        Assert.Equal(3250, details.RootElement.GetProperty("coinsDelta").GetInt32());
    }

    [Fact]
    public async Task RepeatGrant_ReturnsAlreadyDiscoveredAndChangesNothing()
    {
        var service = Service();
        await service.DiscoverAsync(UserId, Regions("district_oldquarter"));
        var before = Alice();
        var auditCount = Audit().Count;

        var again = await service.DiscoverAsync(UserId, Regions("district_oldquarter"));

        Assert.Empty(again.Granted);
        Assert.Equal(new[] { Rivia, OldQuarter }, again.AlreadyDiscovered);
        Assert.Empty(again.Skipped);
        Assert.Equal((0, 0, 0), (again.TotalCoins, again.TotalGems, again.TotalExp));
        Assert.Equal((before.Coins, before.Gems, before.ExperiencePoints), (again.NewCoins, again.NewGems, again.NewExperiencePoints));
        var after = Alice();
        Assert.Equal((before.Coins, before.Gems, before.ExperiencePoints), (after.Coins, after.Gems, after.ExperiencePoints));
        Assert.Equal(2, _db.UserDomainDiscoveries.Count());
        Assert.Equal(auditCount, Audit().Count);
    }

    [Fact]
    public async Task District_AlsoDiscoversItsTown_TopDown()
    {
        var result = await Service().DiscoverAsync(UserId, Regions("district_oldquarter"));

        Assert.Equal(new[] { (Rivia, "Town", "Ancestor"), (OldQuarter, "District", "RegionEnter") },
            result.Granted.Select(g => (g.DomainId, g.DomainType, g.Source)));
        Assert.Equal("Rivia", result.Granted[1].ParentName);
        Assert.Equal(result.Granted.Sum(g => g.Coins), result.TotalCoins);
        // Both in one credit: one BalanceAdjusted row.
        Assert.Single(Audit(), a => a.Action == AuditAction.BalanceAdjusted);
    }

    [Fact]
    public async Task Structure_DiscoversDistrictAndTown_TopDownWithTheTownAsParent()
    {
        var result = await Service().DiscoverAsync(UserId, new DiscoveryGrantRequestDto { WgRegionIds = new() { "structure_smithy" }, Source = "JoinInside" });

        Assert.Equal(new[] { Rivia, OldQuarter, Smithy }, result.Granted.Select(g => g.DomainId));
        Assert.Equal(new[] { "Ancestor", "Ancestor", "JoinInside" }, result.Granted.Select(g => g.Source));
        Assert.Equal(new string?[] { null, "Rivia", "Rivia" }, result.Granted.Select(g => g.ParentName));
        Assert.Equal(new[] { DiscoverySource.Ancestor, DiscoverySource.Ancestor, DiscoverySource.JoinInside },
            _db.UserDomainDiscoveries.AsNoTracking().OrderBy(d => d.DomainId).Select(d => d.Source));
    }

    [Fact]
    public async Task NestedRegionsInOneBatch_KeepTheirOwnSourceAndComeOutTopDown()
    {
        var result = await Service().DiscoverAsync(UserId, Regions("structure_smithy", "town_rivia", "district_oldquarter"));

        Assert.Equal(new[] { Rivia, OldQuarter, Smithy }, result.Granted.Select(g => g.DomainId));
        Assert.All(result.Granted, g => Assert.Equal("RegionEnter", g.Source));
    }

    [Fact]
    public async Task GateStructure_IsDiscoverableAndGemless()
    {
        var result = await Service().DiscoverAsync(UserId, Regions("gate_north"));

        var gate = result.Granted.Single(g => g.DomainId == NorthGate);
        Assert.Equal("GateStructure", gate.DomainType);
        Assert.Equal(0, gate.Gems);
        // 0.15 units x 25 = 3.75 -> 4 XP; 0.15h x 650 = 97.5 -> 98 coins.
        Assert.Equal((98, 4), (gate.Coins, gate.Exp));
    }

    [Fact]
    public async Task IncludeAncestorsOff_DiscoversOnlyTheDomainItself()
    {
        _db.DiscoveryRewardRules.Single(r => r.DomainType == "District").IncludeAncestors = false;
        _db.SaveChanges();

        var result = await Service().DiscoverAsync(UserId, Regions("district_oldquarter"));

        Assert.Equal(new[] { OldQuarter }, result.Granted.Select(g => g.DomainId));
        Assert.Equal("Rivia", result.Granted[0].ParentName);
    }

    [Fact]
    public async Task AncestorAlreadyDiscovered_IsReportedNotRegranted()
    {
        var service = Service();
        await service.DiscoverAsync(UserId, Regions("town_rivia"));

        var result = await service.DiscoverAsync(UserId, Regions("district_oldquarter"));

        Assert.Equal(new[] { OldQuarter }, result.Granted.Select(g => g.DomainId));
        Assert.Equal(new[] { Rivia }, result.AlreadyDiscovered);
    }

    [Fact]
    public async Task DisabledType_IsSkipped()
    {
        _db.DiscoveryRewardRules.Single(r => r.DomainType == "Town").IsEnabled = false;
        _db.SaveChanges();

        var result = await Service().DiscoverAsync(UserId, Regions("district_oldquarter"));

        Assert.Equal(new[] { OldQuarter }, result.Granted.Select(g => g.DomainId));
        var skip = Assert.Single(result.Skipped);
        Assert.Equal((Rivia.ToString(), DiscoverySkipDto.Disabled), (skip.Key, skip.Reason));
    }

    [Fact]
    public async Task DisabledOverride_SkipsThatDomainOnly()
    {
        _db.DomainDiscoveryOverrides.Add(new DomainDiscoveryOverride { DomainId = Smithy, IsEnabled = false });
        _db.SaveChanges();

        var result = await Service().DiscoverAsync(UserId, Regions("structure_smithy", "gate_north"));

        Assert.DoesNotContain(result.Granted, g => g.DomainId == Smithy);
        Assert.Contains(result.Granted, g => g.DomainId == NorthGate);
        Assert.Contains(result.Skipped, s => s.Key == "structure_smithy" && s.Reason == DiscoverySkipDto.Disabled);
        Assert.False(_db.UserDomainDiscoveries.Any(d => d.DomainId == Smithy));
    }

    [Fact]
    public async Task Override_ChangesTheRewardOfOneDomain()
    {
        _db.DomainDiscoveryOverrides.Add(new DomainDiscoveryOverride { DomainId = Smithy, GemsMin = 20, GemsMax = 20, IncludeAncestors = false });
        _db.SaveChanges();

        var result = await Service().DiscoverAsync(UserId, Regions("structure_smithy"));

        var smithy = Assert.Single(result.Granted);
        Assert.Equal((Smithy, 20), (smithy.DomainId, smithy.Gems));
    }

    [Fact]
    public async Task OverrideCanEnableOneDomainOfADisabledType()
    {
        _db.DiscoveryRewardRules.Single(r => r.DomainType == "Town").IsEnabled = false;
        _db.DomainDiscoveryOverrides.Add(new DomainDiscoveryOverride { DomainId = Kardenna, IsEnabled = true });
        _db.SaveChanges();

        var result = await Service().DiscoverAsync(UserId, Regions("town_kardenna", "town_rivia"));

        Assert.Equal(new[] { Kardenna }, result.Granted.Select(g => g.DomainId));
        Assert.Contains(result.Skipped, s => s.Key == "town_rivia" && s.Reason == DiscoverySkipDto.Disabled);
    }

    [Fact]
    public async Task UnknownRegionAndDomainIds_AreNotADomain()
    {
        var result = await Service().DiscoverAsync(UserId, new DiscoveryGrantRequestDto
        {
            WgRegionIds = new() { "spawn", "town_kardenna" },
            DomainIds = new() { 9999 }
        });

        Assert.Equal(new[] { Kardenna }, result.Granted.Select(g => g.DomainId));
        Assert.Equal(new[] { ("spawn", "NotADomain"), ("9999", "NotADomain") }, result.Skipped.Select(s => (s.Key, s.Reason)));
    }

    [Fact]
    public async Task RegionIds_MatchIgnoringCaseAndWhitespace()
    {
        var result = await Service().DiscoverAsync(UserId, Regions("  TOWN_Kardenna ", "town_KARDENNA"));

        var grant = Assert.Single(result.Granted);
        Assert.Equal(Kardenna, grant.DomainId);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task DomainIds_CanBeSentInsteadOfRegions()
    {
        var result = await Service().DiscoverAsync(UserId, new DiscoveryGrantRequestDto { DomainIds = new() { Market }, Source = "Replay" });

        Assert.Equal(new[] { (Kardenna, "Ancestor"), (Market, "Replay") }, result.Granted.Select(g => (g.DomainId, g.Source)));
    }

    [Fact]
    public async Task HourlyCap_GrantsTopDownUpToTheCapAndRateLimitsTheRest()
    {
        var result = await Service(maxNewPerHour: 2).DiscoverAsync(UserId, Regions("structure_smithy"));

        Assert.Equal(new[] { Rivia, OldQuarter }, result.Granted.Select(g => g.DomainId));
        var limited = Assert.Single(result.Skipped);
        Assert.Equal(("structure_smithy", DiscoverySkipDto.RateLimited), (limited.Key, limited.Reason));
        Assert.Equal(2, _db.UserDomainDiscoveries.Count());
    }

    [Fact]
    public async Task HourlyCap_CountsTheTrailingHourOnly()
    {
        _db.UserDomainDiscoveries.Add(new UserDomainDiscovery { UserId = UserId, DomainId = Rivia, DiscoveredAt = DateTime.UtcNow.AddMinutes(-10) });
        _db.UserDomainDiscoveries.Add(new UserDomainDiscovery { UserId = UserId, DomainId = OldQuarter, DiscoveredAt = DateTime.UtcNow.AddHours(-2) });
        _db.SaveChanges();

        var capped = await Service(maxNewPerHour: 1).DiscoverAsync(UserId, Regions("town_kardenna"));
        Assert.Empty(capped.Granted);
        Assert.Equal(DiscoverySkipDto.RateLimited, Assert.Single(capped.Skipped).Reason);
        Assert.Equal(250, Alice().Coins);

        var room = await Service(maxNewPerHour: 2).DiscoverAsync(UserId, Regions("town_kardenna"));
        Assert.Single(room.Granted);
    }

    [Fact]
    public async Task HourlyCapOff_WhenZero()
    {
        var result = await Service(maxNewPerHour: 0).DiscoverAsync(UserId, Regions("structure_smithy", "gate_north", "district_market"));

        Assert.Equal(6, result.Granted.Count); // both towns, both districts, the smithy and the gate
    }

    [Fact]
    public async Task Multipliers_ScaleEachCurrencyWithItsOwnPersonalAndRankMultipliers()
    {
        var alice = _db.Users.Single(u => u.Id == UserId);
        alice.PersonalSalaryMultiplier = 2m;
        alice.PersonalExpBonusMultiplier = 1.5m;
        _db.PermissionGroups.Add(new PermissionGroup { Id = 20, Name = "Royal", IsPremiumTier = true, SalaryMultiplier = 1.2m, GemBonusMultiplier = 2m, ExpBonusMultiplier = 1m, ChatPrimaryColor = "&6" });
        _db.PermissionGroups.Add(new PermissionGroup { Id = 21, Name = "Expired", SalaryMultiplier = 10m, GemBonusMultiplier = 10m, ExpBonusMultiplier = 10m });
        _db.UserPermissionGroups.Add(new UserPermissionGroup { UserId = UserId, PermissionGroupId = 20 });
        _db.UserPermissionGroups.Add(new UserPermissionGroup { UserId = UserId, PermissionGroupId = 21, ExpiresAt = DateTime.UtcNow.AddDays(-1) });
        _db.SaveChanges();

        var result = await Service().DiscoverAsync(UserId, Regions("town_kardenna"));

        var town = Assert.Single(result.Granted);
        Assert.Equal((3250, 10, 63), (town.CoinsBase, town.GemsBase, town.ExpBase));
        // coins x2 personal x1.2 Royal; gems x1 x2 Royal; XP x1.5 personal x1 (63 x 1.5 = 94.5 -> 95).
        Assert.Equal((7800, 20, 95), (town.Coins, town.Gems, town.Exp));
        Assert.Equal((3250, 10, 63), (result.TotalCoinsBase, result.TotalGemsBase, result.TotalExpBase));
        Assert.Equal(new[] { ("personal", 2m), ("rank", 1.2m) }, result.CoinMultipliers.Select(m => (m.Source, m.Value)));
        Assert.Equal(new[] { ("personal", 1m), ("rank", 2m) }, result.GemMultipliers.Select(m => (m.Source, m.Value)));
        Assert.Equal(new[] { ("personal", 1.5m), ("rank", 1m) }, result.ExpMultipliers.Select(m => (m.Source, m.Value)));
        Assert.Equal(("Royal", "&6", true), (result.CoinMultipliers[1].Name, result.CoinMultipliers[1].ChatPrimaryColor, result.CoinMultipliers[1].IsPremiumTier));

        var row = _db.UserDomainDiscoveries.AsNoTracking().Single();
        Assert.Equal((2.4m, 2m, 1.5m), (row.CoinMultiplier, row.GemMultiplier, row.ExpMultiplier));
        Assert.Equal((7800, 20, 95), (row.CoinsAwarded, row.GemsAwarded, row.ExpAwarded));
    }

    [Fact]
    public async Task RewardsScaleWithTheTitle()
    {
        _db.Users.Single(u => u.Id == UserId).ExperiencePoints = 20000; // Count
        _db.SaveChanges();

        var result = await Service().DiscoverAsync(UserId, Regions("town_kardenna"));

        var town = Assert.Single(result.Granted);
        Assert.Equal(10, result.TitleBracketId);
        Assert.Equal((50000, 250), (town.Coins, town.Exp)); // 5h x 10,000; 2.5 x 100
    }

    [Fact]
    public async Task CrossingATitle_ReturnsOneConsolidatedTitleChangeWithoutQueueingANotification()
    {
        _db.Users.Single(u => u.Id == UserId).ExperiencePoints = 2450; // 50 XP short of Peasant
        _db.SaveChanges();

        var result = await Service().DiscoverAsync(UserId, Regions("structure_smithy"));

        Assert.NotNull(result.TitleChange);
        Assert.Equal(("promotion", "Serf", "Peasant"), (result.TitleChange!.Direction, result.TitleChange.FromTitleName, result.TitleChange.ToTitleName));
        Assert.Equal(13500, result.TitleChange.CoinBonusGranted);
        Assert.Equal(2450 + result.TotalExp + 32, result.NewExperiencePoints);
        Assert.Equal(250 + result.TotalCoins + 13500, result.NewCoins);
        Assert.Single(Audit(), a => a.Action == AuditAction.TitleChanged);
        _notifications.Verify(q => q.Enqueue(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TitleChangeResultDto?>()), Times.Never);
    }

    [Fact]
    public async Task EveryAmountRoundingToZero_StillRecordsTheDiscoveryWithoutAnEmptyCredit()
    {
        _db.DomainDiscoveryOverrides.Add(new DomainDiscoveryOverride
        {
            DomainId = Kardenna, ExpUnitsMin = 0, ExpUnitsMax = 0, CoinSalaryHoursMin = 0, CoinSalaryHoursMax = 0, GemsMin = 0, GemsMax = 0
        });
        _db.SaveChanges();

        var result = await Service().DiscoverAsync(UserId, Regions("town_kardenna"));

        Assert.Single(result.Granted);
        Assert.Single(_db.UserDomainDiscoveries);
        Assert.Empty(Audit());
        Assert.Equal(250, result.NewCoins);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("regionenter")]
    [InlineData("JoinInside")]
    [InlineData("Replay")]
    public async Task ClientSources_AreAccepted(string? source)
    {
        var result = await Service().DiscoverAsync(UserId, new DiscoveryGrantRequestDto { WgRegionIds = new() { "town_kardenna" }, Source = source });

        Assert.Single(result.Granted);
    }

    [Theory]
    [InlineData("Ancestor")]
    [InlineData("Admin")]
    [InlineData("0")]
    [InlineData("Teleport")]
    public async Task OtherSources_AreRejected(string source)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().DiscoverAsync(UserId,
            new DiscoveryGrantRequestDto { WgRegionIds = new() { "town_kardenna" }, Source = source }));
    }

    [Fact]
    public async Task EmptyRequest_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().DiscoverAsync(UserId, new DiscoveryGrantRequestDto { WgRegionIds = new() { " ", "" } }));
        await Assert.ThrowsAsync<ArgumentException>(() => Service().DiscoverAsync(UserId, null!));
    }

    [Fact]
    public async Task MoreThanFiftyIds_IsRejected()
    {
        var request = new DiscoveryGrantRequestDto
        {
            WgRegionIds = Enumerable.Range(0, 30).Select(i => $"r{i}").ToList(),
            DomainIds = Enumerable.Range(1000, 21).ToList()
        };

        await Assert.ThrowsAsync<ArgumentException>(() => Service().DiscoverAsync(UserId, request));
        Assert.Empty(_db.UserDomainDiscoveries);
    }

    [Fact]
    public async Task FiftyIds_IsAllowed()
    {
        var request = new DiscoveryGrantRequestDto { WgRegionIds = Enumerable.Range(0, 49).Select(i => $"r{i}").Append("town_kardenna").ToList() };

        var result = await Service().DiscoverAsync(UserId, request);

        Assert.Single(result.Granted);
        Assert.Equal(49, result.Skipped.Count);
    }

    [Fact]
    public async Task UnknownUser_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service().DiscoverAsync(999, Regions("town_kardenna")));
        Assert.Empty(_db.UserDomainDiscoveries);
    }

    [Fact]
    public async Task DuplicateKeyFromARace_IsRetriedOnceAndReportsTheCommittedRowsAsAlreadyDiscovered()
    {
        // Simulates the MySQL unique index rejecting an insert a concurrent grant beat us to: the
        // first AddRangeAsync commits the competing row and then fails like MySQL would.
        var repo = RacingRepository.Create(new DiscoveryRepository(_db), () =>
        {
            _db.ChangeTracker.Clear();
            _db.UserDomainDiscoveries.Add(new UserDomainDiscovery { UserId = UserId, DomainId = Kardenna, DiscoveredAt = DateTime.UtcNow });
            _db.SaveChanges();
        });

        var result = await Service(repo: repo).DiscoverAsync(UserId, Regions("district_market"));

        Assert.Equal(new[] { Kardenna }, result.AlreadyDiscovered);
        Assert.Equal(new[] { Market }, result.Granted.Select(g => g.DomainId));
        Assert.Equal(2, _db.UserDomainDiscoveries.Count());
    }

    [Fact]
    public async Task Known_ListsDomainAndRegionIds()
    {
        var service = Service();
        await service.DiscoverAsync(UserId, Regions("district_oldquarter"));

        var known = await service.GetKnownAsync(UserId);

        Assert.Equal(new[] { (Rivia, "town_rivia"), (OldQuarter, "district_oldquarter") }, known.Select(k => (k.DomainId, k.WgRegionId)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetKnownAsync(999));
    }

    [Fact]
    public async Task Progress_ListsEveryEnabledDomainWithTheUsersState()
    {
        var service = Service();
        await service.DiscoverAsync(UserId, Regions("district_oldquarter"));
        _db.DomainDiscoveryOverrides.Add(new DomainDiscoveryOverride { DomainId = NorthGate, IsEnabled = false });
        _db.SaveChanges();

        var all = await service.GetProgressAsync(UserId, new PagedQueryDto { PageSize = 50 });

        Assert.Equal(5, all.TotalCount); // the disabled gate is left out
        Assert.Equal(new[] { "Kardenna", "Rivia", "Market", "Old Quarter", "Smithy" }, all.Items.Select(r => r.Name));
        var rivia = all.Items.Single(r => r.DomainId == Rivia);
        Assert.True(rivia.Discovered);
        Assert.NotNull(rivia.DiscoveredAt);
        Assert.Equal(DateTimeKind.Utc, rivia.DiscoveredAt!.Value.Kind);
        Assert.True(rivia.Coins > 0);
        Assert.Equal("Kardenna", all.Items.Single(r => r.DomainId == Market).ParentName);

        var undiscovered = await service.GetProgressAsync(UserId, new PagedQueryDto
        {
            PageSize = 50, Filters = new() { ["status"] = "undiscovered", ["domainType"] = "district" }
        });
        Assert.Equal(new[] { Market }, undiscovered.Items.Select(r => r.DomainId));

        var searched = await service.GetProgressAsync(UserId, new PagedQueryDto { SearchTerm = "smith" });
        Assert.Equal(new[] { Smithy }, searched.Items.Select(r => r.DomainId));

        var paged = await service.GetProgressAsync(UserId, new PagedQueryDto { PageNumber = 2, PageSize = 2 });
        Assert.Equal((5, 2), (paged.TotalCount, paged.Items.Count));
        Assert.Equal(new[] { "Market", "Old Quarter" }, paged.Items.Select(r => r.Name));
    }

    [Fact]
    public async Task Summary_CountsPerTypeLatestAndLifetimeTotals()
    {
        var service = Service();
        var first = await service.DiscoverAsync(UserId, Regions("structure_smithy"));

        var summary = await service.GetSummaryAsync(UserId);

        Assert.Equal(new[] { ("Town", 1, 2), ("District", 1, 2), ("Structure", 1, 1), ("GateStructure", 0, 1) },
            summary.ByType.Select(t => (t.DomainType, t.Discovered, t.Total)));
        Assert.Equal(3, summary.TotalDiscovered);
        Assert.Equal((first.TotalCoins, first.TotalGems, first.TotalExp), (summary.TotalCoins, summary.TotalGems, summary.TotalExp));
        Assert.NotNull(summary.Latest);
    }

    [Fact]
    public async Task Reset_DeletesTheRowAuditsItAndLetsTheDomainBeDiscoveredAgain()
    {
        var service = Service();
        await service.DiscoverAsync(UserId, Regions("town_kardenna"));
        var coinsAfterFirst = Alice().Coins;

        Assert.True(await service.ResetAsync(UserId, Kardenna, actorUserId: 101));

        Assert.Empty(_db.UserDomainDiscoveries);
        Assert.Equal(coinsAfterFirst, Alice().Coins); // no claw-back
        var reset = Audit().Last();
        Assert.Equal((AuditAction.DiscoveryReset, (int?)101), (reset.Action, reset.ActorUserId));
        Assert.Contains("Kardenna", reset.Details);

        var again = await service.DiscoverAsync(UserId, Regions("town_kardenna"));
        Assert.Single(again.Granted);
    }

    [Fact]
    public async Task Reset_OfSomethingNotDiscoveredIsFalse()
    {
        Assert.False(await Service().ResetAsync(UserId, Kardenna, null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service().ResetAsync(999, Kardenna, null));
    }

    [Fact]
    public async Task Stats_CountDiscoverersFirstDiscovererAndTopExplorers()
    {
        var service = Service();
        await service.DiscoverAsync(UserId, Regions("town_rivia"));
        await service.DiscoverAsync(101, Regions("structure_smithy"));

        var stats = await service.GetStatsAsync(new PagedQueryDto { PageSize = 50, SortDescending = true });

        Assert.Equal(2, stats.LinkedUserCount);
        var rivia = stats.Domains.Items.First();
        Assert.Equal((Rivia, 2, 100m, UserId, "alice"), (rivia.DomainId, rivia.Discoverers, rivia.DiscovererPercent, rivia.FirstDiscovererUserId, rivia.FirstDiscovererUsername));
        var smithy = stats.Domains.Items.Single(s => s.DomainId == Smithy);
        Assert.Equal((1, 50m, "bob"), (smithy.Discoverers, smithy.DiscovererPercent, smithy.FirstDiscovererUsername));
        Assert.Equal(0, stats.Domains.Items.Single(s => s.DomainId == Kardenna).Discoverers);
        Assert.Equal(new[] { (101, "bob", 3), (UserId, "alice", 1) }, stats.TopExplorers.Select(t => (t.UserId, t.Username, t.Discoveries)));

        var towns = await service.GetStatsAsync(new PagedQueryDto { Filters = new() { ["domainType"] = "Town" } });
        Assert.Equal(2, towns.Domains.TotalCount);
        Assert.Equal(Kardenna, towns.Domains.Items.First().DomainId); // least discovered first
    }

    /// <summary>Wraps the real repository; the first AddRangeAsync runs <c>race</c> and then throws
    /// the DbUpdateException MySQL's unique index would.</summary>
    public class RacingRepository : DispatchProxy
    {
        private IDiscoveryRepository _inner = null!;
        private Action _race = null!;
        private bool _raced;

        public static IDiscoveryRepository Create(IDiscoveryRepository inner, Action race)
        {
            var proxy = Create<IDiscoveryRepository, RacingRepository>();
            var racing = (RacingRepository)(object)proxy;
            racing._inner = inner;
            racing._race = race;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IDiscoveryRepository.AddRangeAsync) && !_raced)
            {
                _raced = true;
                _race();
                return Task.FromException(new DbUpdateException("save failed",
                    new Exception("Duplicate entry '100-5' for key 'IX_user_domain_discoveries_UserId_DomainId'")));
            }
            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }
    }
}
