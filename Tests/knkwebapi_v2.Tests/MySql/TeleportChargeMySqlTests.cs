using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Warp charges on a real MySQL (teleport IMPLEMENTATION_PLAN.md Phase 5): the plugin retries a
/// charge with the same idempotency key after a timeout, so concurrent same-key charges must take
/// the gems exactly once; different keys racing for too few gems must never overdraw; a refund
/// racing its own charge must end either refunded or void, never charged-without-teleport; and the
/// AddDomainTeleportSettings migration's FK/check constraint hold.
/// </summary>
[Trait("Category", "requires-mysql")]
public class TeleportChargeMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    public TeleportChargeMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private TeleportDestinationService Service(KnKDbContext ctx)
    {
        var users = new UserRepository(ctx);
        var groups = new UserPermissionGroupService(new UserPermissionGroupRepository(ctx), users,
            new PermissionGroupRepository(ctx), new Mock<IAuditLogService>().Object);
        return new TeleportDestinationService(new TeleportDestinationRepository(ctx), users, groups,
            new DiscoveryRepository(ctx), new TitleBracketRepository(ctx), new PermissionGroupRepository(ctx),
            new CurrencyService(new CurrencyRepository(ctx), users, NullLogger<CurrencyService>.Instance),
            new CurrencyRepository(ctx), _cache, NullLogger<TeleportDestinationService>.Instance);
    }

    private async Task<int> UserAsync(int gems)
    {
        await using var ctx = _db.NewContext();
        var user = new User { Username = "u" + Guid.NewGuid().ToString("N")[..12], Gems = gems };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> TownAsync(int price)
    {
        await using var ctx = _db.NewContext();
        var name = "t" + Guid.NewGuid().ToString("N")[..10];
        var town = new Town
        {
            Name = name, Description = "", WgRegionId = name, Location = new Location { World = "world", X = 1, Y = 70, Z = 1 },
            TeleportEnabled = true, TeleportPriceGems = price
        };
        ctx.Towns.Add(town);
        await ctx.SaveChangesAsync();
        return town.Id;
    }

    private async Task<int> GemsAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).Gems;
    }

    private async Task<int> TeleportFeesAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.CurrencyEntries.CountAsync(e => e.UserId == userId && e.Transaction.ReasonCode == CurrencyReasons.TeleportFee);
    }

    /// <summary>Runs the calls at once, each on its own DbContext (= its own connection/request).</summary>
    private async Task<List<(T? Result, Exception? Error)>> InParallelAsync<T>(int count, Func<TeleportDestinationService, int, Task<T>> call)
        where T : class
    {
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await start.Task;
            await using var ctx = _db.NewContext();
            try
            {
                return ((T?)await call(Service(ctx), i), (Exception?)null);
            }
            catch (Exception ex)
            {
                return ((T?)null, ex);
            }
        }).ToList();
        start.SetResult();
        return (await Task.WhenAll(tasks)).ToList();
    }

    private static TeleportChargeRequestDto Charge(int userId, string key) => new() { UserId = userId, IdempotencyKey = key };

    [MySqlFact]
    public async Task TwelveParallelChargesWithOneKey_TakeTheGemsOnce()
    {
        var user = await UserAsync(gems: 100);
        var town = await TownAsync(price: 10);
        var key = "warp:" + Guid.NewGuid().ToString("N");

        var outcomes = await InParallelAsync(12, (s, _) => s.ChargeAsync(town, Charge(user, key)));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.Single(outcomes, o => !o.Result!.Replayed);
        Assert.All(outcomes, o => Assert.Equal(10, o.Result!.Charged));
        Assert.Single(outcomes.Select(o => o.Result!.TransactionPublicId).Distinct());
        Assert.Equal(90, await GemsAsync(user));
        Assert.Equal(1, await TeleportFeesAsync(user));
    }

    [MySqlFact]
    public async Task TwentyParallelWarpsWithOwnKeys_NeverOverdraw()
    {
        var user = await UserAsync(gems: 35);
        var town = await TownAsync(price: 10);

        var outcomes = await InParallelAsync(20, (s, i) => s.ChargeAsync(town, Charge(user, $"warp:{user}:{i}")));

        Assert.Equal(3, outcomes.Count(o => o.Result != null));
        Assert.All(outcomes.Where(o => o.Error != null), o =>
            Assert.Equal(TeleportDestinationException.InsufficientGems, Assert.IsType<TeleportDestinationException>(o.Error).Code));
        Assert.Equal(5, await GemsAsync(user));
        Assert.Equal(3, await TeleportFeesAsync(user));
    }

    [MySqlFact]
    public async Task ChargeAndRefundRacing_EndRefundedOrVoid_NeverCharged()
    {
        for (var round = 0; round < 10; round++)
        {
            var user = await UserAsync(gems: 50);
            var town = await TownAsync(price: 10);
            var key = $"warp:race:{user}";

            var outcomes = await InParallelAsync<object>(2, async (s, i) => i == 0
                ? await s.ChargeAsync(town, Charge(user, key))
                : await s.RefundAsync(new TeleportRefundRequestDto { UserId = user, IdempotencyKey = key }));

            // Either the refund ran first (charge refused: key void) or the charge did (then refunded).
            Assert.Null(outcomes[1].Error);
            Assert.Equal(50, await GemsAsync(user));
            var refund = Assert.IsType<TeleportRefundResultDto>(outcomes[1].Result);
            if (refund.Refunded)
            {
                Assert.IsType<TeleportChargeResultDto>(outcomes[0].Result);
            }
            else
            {
                Assert.Equal(TeleportDestinationException.Refunded, Assert.IsType<TeleportDestinationException>(outcomes[0].Error).Code);
            }
        }
    }

    [MySqlFact]
    public async Task ParallelRefundsOfOneCharge_ReverseItOnce()
    {
        var user = await UserAsync(gems: 20);
        var town = await TownAsync(price: 10);
        var key = "warp:" + Guid.NewGuid().ToString("N");
        await using (var ctx = _db.NewContext())
        {
            await Service(ctx).ChargeAsync(town, Charge(user, key));
        }

        var outcomes = await InParallelAsync(8, (s, _) => s.RefundAsync(new TeleportRefundRequestDto { UserId = user, IdempotencyKey = key }));

        Assert.All(outcomes, o => Assert.True(o.Result!.Refunded));
        Assert.Single(outcomes, o => !o.Result!.Replayed);
        Assert.Equal(20, await GemsAsync(user));
        await using var check = _db.NewContext();
        Assert.Equal(1, await check.CurrencyTransactions.CountAsync(t => t.ReasonCode == CurrencyReasons.Reversal
            && t.Entries.Any(e => e.UserId == user)));
    }

    [MySqlFact]
    public async Task Migration_PriceCheckConstraint_AndSetNullForeignKeys()
    {
        var town = await TownAsync(price: 0);
        await using var ctx = _db.NewContext();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            ctx.Database.ExecuteSqlInterpolatedAsync($"UPDATE domains SET TeleportPriceGems = -1 WHERE Id = {town}"));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            ctx.Database.ExecuteSqlInterpolatedAsync($"UPDATE domains SET TeleportPriceGems = {BalanceLimits.MaxGems + 1} WHERE Id = {town}"));

        var bracket = new TitleBracket { MaleName = "TestTitle", FemaleName = "TestTitle", MinExperience = 123456789 };
        var group = new PermissionGroup { Name = "tp-" + Guid.NewGuid().ToString("N")[..8], IsPremiumTier = true, Weight = 5 };
        ctx.TitleBrackets.Add(bracket);
        ctx.PermissionGroups.Add(group);
        await ctx.SaveChangesAsync();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE domains SET TeleportMinTitleBracketId = {bracket.Id}, TeleportMinPremiumGroupId = {group.Id} WHERE Id = {town}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM title_brackets WHERE Id = {bracket.Id}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM permission_groups WHERE Id = {group.Id}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM permission_holders WHERE Id = {group.Id}");

        await using var check = _db.NewContext();
        var domain = await check.Domains.AsNoTracking().SingleAsync(d => d.Id == town);
        Assert.Null(domain.TeleportMinTitleBracketId);
        Assert.Null(domain.TeleportMinPremiumGroupId);
    }
}
