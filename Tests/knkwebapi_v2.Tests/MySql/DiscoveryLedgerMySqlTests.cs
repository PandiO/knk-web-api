using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Discovery rewards through the currency ledger on a real MySQL (domain-discovery
/// IMPLEMENTATION_PLAN.md Phase 5): the user row lock, the discovery rows and the ledger postings
/// share one transaction, so concurrent identical grants post once per domain, a refused credit
/// leaves no row behind, and the reconciler finds nothing. Uses the migrated seed data (reward
/// rules, title brackets); every test has its own user and domains.
/// </summary>
[Trait("Category", "requires-mysql")]
public class DiscoveryLedgerMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public DiscoveryLedgerMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static DiscoveryService Service(KnKDbContext ctx)
    {
        var userRepo = new UserRepository(ctx);
        var groupRepo = new PermissionGroupRepository(ctx);
        var audit = new AuditLogService(new AuditLogRepository(ctx), userRepo);
        var titles = new TitleService(new TitleBracketRepository(ctx));
        var memberships = new UserPermissionGroupService(new UserPermissionGroupRepository(ctx), userRepo, groupRepo, audit);
        var users = new UserService(userRepo, new Mock<IMapper>().Object, new Mock<IPasswordService>().Object,
            new Mock<ILinkCodeService>().Object, titles, memberships, audit, groupRepo,
            NullLogger<UserService>.Instance, new Mock<IPlayerNotificationQueue>().Object);
        var currency = new CurrencyService(new CurrencyRepository(ctx), userRepo, NullLogger<CurrencyService>.Instance);
        return new DiscoveryService(new DiscoveryRepository(ctx), userRepo, users, currency, titles, memberships, audit,
            NullLogger<DiscoveryService>.Instance, Options.Create(new DiscoveryOptions { MaxNewPerHour = 120 }));
    }

    /// <summary>A user and a fresh Town with one District; returns (userId, districtRegion, townId, districtId).</summary>
    private async Task<(int UserId, string DistrictRegion, int TownId, int DistrictId)> SeedAsync(int coins = 0, int experience = 0)
    {
        var tag = Guid.NewGuid().ToString("N")[..10];
        await using var ctx = _db.NewContext();
        var user = new User { Username = "d" + tag, Coins = coins, Gems = 0, ExperiencePoints = experience };
        var town = new Town { Name = "Town " + tag, Description = "", WgRegionId = "town_" + tag };
        ctx.Users.Add(user);
        ctx.Towns.Add(town);
        await ctx.SaveChangesAsync();
        var district = new District { Name = "District " + tag, Description = "", WgRegionId = "district_" + tag, TownId = town.Id };
        ctx.Districts.Add(district);
        await ctx.SaveChangesAsync();
        return (user.Id, district.WgRegionId, town.Id, district.Id);
    }

    private static DiscoveryGrantRequestDto Regions(params string[] ids) => new() { WgRegionIds = ids.ToList() };

    private async Task<List<CurrencyTransaction>> PostingsAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        var prefix = $"discovery:{userId}:";
        return await ctx.CurrencyTransactions.AsNoTracking()
            .Include(t => t.Entries)
            .Where(t => t.IdempotencyKey.StartsWith(prefix))
            .OrderBy(t => t.Id)
            .ToListAsync();
    }

    private async Task<List<CurrencyMismatchDto>> MismatchesAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await new CurrencyReconciler(ctx).FindMismatchesAsync(userId);
    }

    [MySqlFact]
    public async Task TenConcurrentIdenticalGrants_OneSetOfRows_OnePostingPerDomain_ReconcilerClean()
    {
        var (userId, region, townId, districtId) = await SeedAsync(coins: 250);

        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            await start.Task;
            await using var ctx = _db.NewContext();
            return await Service(ctx).DiscoverAsync(userId, Regions(region));
        }).ToList();
        start.SetResult();
        var results = await Task.WhenAll(tasks);

        var winner = Assert.Single(results, r => r.Granted.Count > 0);
        Assert.Equal(new[] { townId, districtId }, winner.Granted.Select(g => g.DomainId));
        Assert.All(results.Where(r => r != winner), r =>
        {
            Assert.Empty(r.Granted);
            Assert.Equal(new[] { townId, districtId }, r.AlreadyDiscovered);
        });

        await using var ctx = _db.NewContext();
        Assert.Equal(2, await ctx.UserDomainDiscoveries.CountAsync(d => d.UserId == userId));
        var postings = await PostingsAsync(userId);
        Assert.Equal(new[] { $"discovery:{userId}:{townId}", $"discovery:{userId}:{districtId}" }, postings.Select(p => p.IdempotencyKey));
        Assert.All(postings, p => Assert.Equal(CurrencyReasons.DiscoveryReward, p.ReasonCode));

        var user = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal((250 + winner.TotalCoins, winner.TotalGems, winner.TotalExp), (user.Coins, user.Gems, user.ExperiencePoints));
        Assert.Equal((user.Coins, user.Gems, user.ExperiencePoints), (winner.NewCoins, winner.NewGems, winner.NewExperiencePoints));
        Assert.Empty(await MismatchesAsync(userId));
    }

    [MySqlFact]
    public async Task ResetAndRediscover_PostsUnderTheNextKey_ReconcilerClean()
    {
        var (userId, region, townId, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            await Service(ctx).DiscoverAsync(userId, Regions(region));
        }
        await using (var ctx = _db.NewContext())
        {
            Assert.True(await Service(ctx).ResetAsync(userId, townId, actorUserId: null));
        }

        DiscoveryGrantResultDto again;
        await using (var ctx = _db.NewContext())
        {
            again = await Service(ctx).DiscoverAsync(userId, Regions(region));
        }

        Assert.Equal(new[] { townId }, again.Granted.Select(g => g.DomainId));
        Assert.Contains($"discovery:{userId}:{townId}:2", (await PostingsAsync(userId)).Select(p => p.IdempotencyKey));
        Assert.Empty(await MismatchesAsync(userId));
    }

    [MySqlFact]
    public async Task LedgerRefusal_RollsBackTheDiscoveryRows()
    {
        var (userId, region, _, _) = await SeedAsync(coins: BalanceLimits.MaxCoins);

        await using (var ctx = _db.NewContext())
        {
            var ex = await Assert.ThrowsAsync<CurrencyException>(() => Service(ctx).DiscoverAsync(userId, Regions(region)));
            Assert.Equal(CurrencyErrorCode.BalanceCapExceeded, ex.Code);
        }

        await using var check = _db.NewContext();
        Assert.Equal(0, await check.UserDomainDiscoveries.CountAsync(d => d.UserId == userId));
        Assert.Empty(await PostingsAsync(userId));
        Assert.Equal(BalanceLimits.MaxCoins, (await check.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).Coins);
    }

    [MySqlFact]
    public async Task TitleCrossing_PostsTheXpOnceAndAppliesTheBonusOnce()
    {
        // 50 XP short of Peasant (seeded brackets): the Town's XP crosses it.
        var (userId, region, _, _) = await SeedAsync(experience: 2450);

        DiscoveryGrantResultDto result;
        await using (var ctx = _db.NewContext())
        {
            result = await Service(ctx).DiscoverAsync(userId, Regions(region));
        }

        Assert.NotNull(result.TitleChange);
        Assert.Equal("promotion", result.TitleChange!.Direction);
        var xpPosted = (await PostingsAsync(userId)).SelectMany(p => p.Entries)
            .Where(e => e.AccountKind == CurrencyAccountKind.User && e.Currency == Currency.Experience)
            .Sum(e => e.Amount);
        Assert.Equal(result.TotalExp, xpPosted);
        await using var check = _db.NewContext();
        var user = await check.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal(2450 + result.TotalExp + result.TitleChange.ExpBonusGranted, user.ExperiencePoints);
        Assert.Equal(result.TotalCoins + result.TitleChange.CoinBonusGranted, user.Coins);
        Assert.Equal(1, await check.AuditLogEntries.CountAsync(a => a.TargetUserId == userId && a.Action == AuditAction.TitleChanged));
    }
}
