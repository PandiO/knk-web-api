using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// KNG-81 on a real MySQL: concurrent grant-first-join calls for one player (the plugin firing it
/// twice on a quick relog) each run in their own request and DbContext. The user row lock makes
/// the second wait for the first, then see FirstJoinKitsGrantedAt and grant nothing, so each
/// first-join kit is claimed once and its lootbox tokens are issued once.
/// </summary>
[Trait("Category", "requires-mysql")]
public class FirstJoinKitsMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public FirstJoinKitsMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static readonly IMapper Mapper =
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(UserMappingProfile).Assembly)).CreateMapper();

    private KitService NewKitService(KnKDbContext ctx, ILootboxTokenGrantService lootbox)
    {
        var userRepo = new UserRepository(ctx);
        var currency = new CurrencyService(new CurrencyRepository(ctx), userRepo, NullLogger<CurrencyService>.Instance);
        var audit = new AuditLogService(new AuditLogRepository(ctx), userRepo);
        var groupRepo = new PermissionGroupRepository(ctx);
        var memberships = new UserPermissionGroupService(new UserPermissionGroupRepository(ctx), userRepo, groupRepo, audit);
        return new KitService(new KitRepository(ctx), userRepo, new ItemBlueprintRepository(ctx), new TitleBracketRepository(ctx),
            groupRepo, new TitleService(new TitleBracketRepository(ctx)), memberships,
            new Mock<IPermissionResolutionService>().Object, audit, Mapper, currency, lootbox);
    }

    private async Task<(int UserId, int KitId)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var user = new User { Username = "fj" + Guid.NewGuid().ToString("N")[..10] };
        var kit = new Kit { Name = "Starter " + Guid.NewGuid().ToString("N")[..8], GrantOnFirstJoin = true };
        ctx.Users.Add(user);
        ctx.Kits.Add(kit);
        await ctx.SaveChangesAsync();
        return (user.Id, kit.Id);
    }

    [MySqlFact]
    public async Task ConcurrentCalls_ClaimEachFirstJoinKitOnce()
    {
        var (userId, kitId) = await SeedAsync();
        var lootbox = new Mock<ILootboxTokenGrantService>();
        const int callers = 6;

        var contexts = Enumerable.Range(0, callers).Select(_ => _db.NewContext()).ToList();
        try
        {
            using var start = new ManualResetEventSlim(false);
            var calls = contexts.Select(ctx => Task.Run(async () =>
            {
                start.Wait();
                return await NewKitService(ctx, lootbox.Object).GrantFirstJoinKitsAsync(userId);
            })).ToList();
            start.Set();
            var results = await Task.WhenAll(calls);

            // Exactly one call granted the kits; every other one saw the flag.
            Assert.Equal(1, results.Count(r => r.Any(k => k.KitId == kitId)));
        }
        finally
        {
            foreach (var ctx in contexts) await ctx.DisposeAsync();
        }

        await using var check = _db.NewContext();
        var claimsPerKit = await check.KitClaims.AsNoTracking()
            .Where(c => c.UserId == userId)
            .GroupBy(c => c.KitId)
            .Select(g => new { KitId = g.Key, Count = g.Count() })
            .ToListAsync();
        Assert.Contains(claimsPerKit, c => c.KitId == kitId);
        Assert.All(claimsPerKit, c => Assert.Equal(1, c.Count));
        Assert.NotNull((await check.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).FirstJoinKitsGrantedAt);
        lootbox.Verify(l => l.IssueForKitAsync(userId, kitId, It.IsAny<int>(), null), Times.Once);
    }

    [MySqlFact]
    public async Task RepeatedCall_InANewRequest_GrantsNothing()
    {
        var (userId, kitId) = await SeedAsync();
        var lootbox = new Mock<ILootboxTokenGrantService>();

        await using (var first = _db.NewContext())
        {
            Assert.Contains(await NewKitService(first, lootbox.Object).GrantFirstJoinKitsAsync(userId), k => k.KitId == kitId);
        }
        await using (var second = _db.NewContext())
        {
            Assert.Empty(await NewKitService(second, lootbox.Object).GrantFirstJoinKitsAsync(userId));
        }

        await using var check = _db.NewContext();
        Assert.Equal(1, await check.KitClaims.CountAsync(c => c.UserId == userId && c.KitId == kitId));
        lootbox.Verify(l => l.IssueForKitAsync(userId, kitId, It.IsAny<int>(), null), Times.Once);
    }
}
