using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Xunit;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Stock;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Stock ledger guarantees only a real MySQL can prove (production DESIGN § 4.5, § 16.1, acceptance A01, A22;
/// PandiO/knk-workspace#97): row locks in id order serialize concurrent moves without deadlocks or lost updates, the
/// idempotency key applies a request once even across storages, a move enlists in the caller's transaction, the
/// holder check constraint holds, the migration seeds the system storages, and the roll-up keeps the totals.
/// Every test uses its own storages, one migrated database per class.
/// </summary>
[Trait("Category", "requires-mysql")]
public class StockLedgerMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public StockLedgerMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static StockContext Ctx(StockReason reason = StockReason.Distribution, string? key = null) =>
        new() { Reason = reason, ActorKind = StockActorKind.System, ActorComponent = "MySqlTest", IdempotencyKey = key };

    private async Task<(int Blueprint, int[] Storages)> SeedAsync(int storages, long? maxUnits = null)
    {
        await using var ctx = _db.NewContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var blueprint = new ItemBlueprint { Name = "Oak " + suffix, DefaultDisplayName = "Oak" };
        ctx.ItemBlueprints.Add(blueprint);
        var created = new List<Storage>();
        for (int i = 0; i < storages; i++)
        {
            var user = new User { Username = $"s{i}_{suffix}" };
            ctx.Users.Add(user);
            var storage = new Storage { Name = $"S{i}", Kind = StorageKind.Player, User = user, MaxUnits = maxUnits };
            ctx.Storages.Add(storage);
            created.Add(storage);
        }
        await ctx.SaveChangesAsync();
        return (blueprint.Id, created.Select(s => s.Id).ToArray());
    }

    private async Task<StockResult> Adjust(int storage, int blueprint, long delta)
    {
        await using var ctx = _db.NewContext();
        return await new StockService(ctx).AdjustAsync(storage, blueprint, null, delta,
            new StockContext { Reason = StockReason.AdminAdjust, ActorKind = StockActorKind.System, Note = "seed" });
    }

    private async Task<long> AmountAsync(int storage, int blueprint)
    {
        await using var ctx = _db.NewContext();
        return await ctx.StockLines.Where(l => l.StorageId == storage && l.ItemBlueprintId == blueprint).Select(l => l.Amount)
            .FirstOrDefaultAsync();
    }

    /// <summary>Runs every call at once (at most <paramref name="parallel"/> in flight), each on its own DbContext.</summary>
    private async Task<List<(StockResult? Result, Exception? Error)>> InParallelAsync(int count, int parallel,
        Func<IStockService, int, Task<StockResult>> call)
    {
        var gate = new SemaphoreSlim(parallel);
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await start.Task;
            await gate.WaitAsync();
            try
            {
                await using var ctx = _db.NewContext();
                return ((StockResult?)await call(new StockService(ctx), i), (Exception?)null);
            }
            catch (Exception ex)
            {
                return ((StockResult?)null, ex);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        start.SetResult();
        return (await Task.WhenAll(tasks)).ToList();
    }

    [MySqlFact]
    public async Task Migration_SeedsTheFiveSystemStorages()
    {
        await using var ctx = _db.NewContext();
        var keys = await ctx.Storages.Where(s => s.Kind == StorageKind.Virtual).OrderBy(s => s.Id).Select(s => s.SystemKey).ToListAsync();

        Assert.Equal(Storage.SystemKeys, keys);
        Assert.Equal(1m, (await ctx.Database.SqlQueryRaw<decimal>("SELECT DEFAULT(StorageWeight) AS Value FROM item_blueprints LIMIT 1")
            .ToListAsync()).DefaultIfEmpty(1m).First());
    }

    [MySqlFact]
    public async Task AThousandConcurrentMoves_LoseNoUpdate_AndTheReconcilerIsClean()
    {
        var (blueprint, storages) = await SeedAsync(4);
        foreach (var s in storages) await Adjust(s, blueprint, 1000);
        var random = new Random(97);
        var plan = Enumerable.Range(0, 1000).Select(_ =>
        {
            int from = random.Next(4), to = (from + 1 + random.Next(3)) % 4;
            return (From: storages[from], To: storages[to], Amount: (long)random.Next(1, 60));
        }).ToList();

        var outcomes = await InParallelAsync(plan.Count, 32, (s, i) =>
            s.MoveAsync(StockLineRequest.Fungible(plan[i].From, plan[i].To, blueprint, plan[i].Amount), Ctx(), StockMoveMode.Partial));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        // Expected amounts from the accepted parts: every accepted unit left one storage and reached another.
        var expected = storages.ToDictionary(s => s, _ => 1000L);
        foreach (var (o, i) in outcomes.Select((o, i) => (o, i)))
        {
            expected[plan[i].From] -= o.Result!.Accepted;
            expected[plan[i].To] += o.Result.Accepted;
        }
        foreach (var s in storages) Assert.Equal(expected[s], await AmountAsync(s, blueprint));
        Assert.Equal(4000L, expected.Values.Sum());

        await using var ctx = _db.NewContext();
        var report = await new StockReconciler(ctx).RunAsync();
        Assert.True(report.Clean, System.Text.Json.JsonSerializer.Serialize(report));
    }

    [MySqlFact]
    public async Task FiftyParallelMovesOfTen_FromOneHundred_ExactlyTenSucceed()
    {
        var (blueprint, storages) = await SeedAsync(3);
        await Adjust(storages[0], blueprint, 100);

        var outcomes = await InParallelAsync(50, 50, (s, i) =>
            s.MoveAsync(StockLineRequest.Fungible(storages[0], storages[1 + i % 2], blueprint, 10), Ctx()));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.Equal(10, outcomes.Count(o => o.Result!.Status == StockResultStatus.Applied));
        Assert.Equal(40, outcomes.Count(o => o.Result!.Lines[0].RejectReason == StockRejectReason.NotEnoughStock));
        Assert.Equal(0, await AmountAsync(storages[0], blueprint));
        Assert.Equal(100, await AmountAsync(storages[1], blueprint) + await AmountAsync(storages[2], blueprint));
    }

    [MySqlFact]
    public async Task ParallelMovesIntoACappedStorage_NeverPassTheCap()
    {
        var (blueprint, sources) = await SeedAsync(4);
        var (_, capped) = await SeedAsync(1, maxUnits: 100);
        foreach (var s in sources) await Adjust(s, blueprint, 100);

        var outcomes = await InParallelAsync(40, 40, (s, i) =>
            s.MoveAsync(StockLineRequest.Fungible(sources[i % 4], capped[0], blueprint, 7), Ctx(), StockMoveMode.Partial));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.Equal(100, await AmountAsync(capped[0], blueprint));
        Assert.Equal(100, outcomes.Sum(o => o.Result!.Accepted));
    }

    [MySqlFact]
    public async Task TwentyParallelIdenticalRequests_ApplyOnce()
    {
        var (blueprint, storages) = await SeedAsync(2);
        await Adjust(storages[0], blueprint, 100);
        var key = "same:" + Guid.NewGuid().ToString("N");

        var outcomes = await InParallelAsync(20, 20, (s, _) =>
            s.MoveAsync(StockLineRequest.Fungible(storages[0], storages[1], blueprint, 30), Ctx(key: key)));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.Single(outcomes.Select(o => o.Result!.TransactionId).Distinct());
        Assert.Equal(19, outcomes.Count(o => o.Result!.Replayed));
        Assert.Equal(30, await AmountAsync(storages[1], blueprint));
    }

    [MySqlFact]
    public async Task OneKeyAcrossDifferentStorages_TheUniqueIndexLetsOneApply()
    {
        var (blueprint, storages) = await SeedAsync(6);
        foreach (var s in storages) await Adjust(s, blueprint, 50);
        var key = "cross:" + Guid.NewGuid().ToString("N");

        // Different storage pairs don't wait on each other's locks; the unique index on the key decides.
        var outcomes = await InParallelAsync(3, 3, (s, i) =>
            s.MoveAsync(StockLineRequest.Fungible(storages[2 * i], storages[2 * i + 1], blueprint, 5), Ctx(key: key)));

        int applied = outcomes.Count(o => o.Result is { Replayed: false, Status: StockResultStatus.Applied });
        Assert.Equal(1, applied);
        Assert.All(outcomes.Where(o => o.Result is not { Replayed: false }), o =>
            Assert.True(o.Error is StockIdempotencyConflictException, o.Error?.ToString()));
        await using var ctx = _db.NewContext();
        Assert.Equal(1, await ctx.StockTransactions.CountAsync(t => t.IdempotencyKey == key));
    }

    [MySqlFact]
    public async Task AMoveInsideTheCallersTransaction_IsRolledBackWithIt()
    {
        var (blueprint, storages) = await SeedAsync(2);
        await Adjust(storages[0], blueprint, 20);

        await using (var ctx = _db.NewContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            var result = await new StockService(ctx).MoveAsync(StockLineRequest.Fungible(storages[0], storages[1], blueprint, 20), Ctx());
            Assert.Equal(StockResultStatus.Applied, result.Status);
            await tx.RollbackAsync();
        }

        Assert.Equal(20, await AmountAsync(storages[0], blueprint));
        Assert.Equal(0, await AmountAsync(storages[1], blueprint));
    }

    [MySqlFact]
    public async Task TheHolderCheckConstraint_RefusesAStorageWithTwoHolders()
    {
        var (_, storages) = await SeedAsync(1);
        await using var ctx = _db.NewContext();
        var userId = await ctx.Storages.Where(s => s.Id == storages[0]).Select(s => s.UserId).SingleAsync();

        var ex = await Assert.ThrowsAnyAsync<MySqlException>(() => ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO storages (Name, Kind, Purpose, UserId, SystemKey, TotalUnits, DistinctItems, RowVersion, CreatedAt) VALUES ('bad', 'Player', 'General', {userId}, 'SYS_BAD', 0, 0, 0, NOW(6))"));
        Assert.Contains("CK_storages_Holder", ex.Message);
    }

    [MySqlFact]
    public async Task Rollup_ReplacesOldProductionMoves_AndKeepsTheReconcilerClean()
    {
        var (blueprint, storages) = await SeedAsync(1);
        int production;
        await using (var ctx = _db.NewContext()) production = await new StockService(ctx).GetSystemStorageIdAsync(Storage.SysProduction);
        var old = new TestClockMySql { Now = DateTimeOffset.UtcNow.AddDays(-45) };
        for (int i = 0; i < 3; i++)
        {
            await using var ctx = _db.NewContext();
            await new StockService(ctx, old).MoveAsync(StockLineRequest.Fungible(production, storages[0], blueprint, 4), Ctx(StockReason.Production));
        }

        StockRollupResult result;
        await using (var ctx = _db.NewContext()) result = await new StockRollupService(ctx).RollUpAsync();

        Assert.True(result.TransactionsRolledUp >= 3);
        Assert.Equal(12, await AmountAsync(storages[0], blueprint));
        await using (var ctx = _db.NewContext())
        {
            Assert.False(await ctx.StockMoves.AnyAsync(m => m.ToStorageId == storages[0]));
            Assert.Equal(12, await ctx.StockMoveRollups.Where(r => r.ToStorageId == storages[0]).SumAsync(r => r.Amount));
            Assert.True((await new StockReconciler(ctx).RunAsync()).Clean);
        }
    }

    private sealed class TestClockMySql : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
