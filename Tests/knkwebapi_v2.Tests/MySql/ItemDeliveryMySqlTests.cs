using Microsoft.EntityFrameworkCore;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Item delivery guarantees only a real MySQL can prove (P7, PandiO/knk-workspace#60, DESIGN § 4.6): concurrent first
/// deliveries create one inbox (the user row lock), a key delivers once under concurrency, and concurrent deliveries
/// never put more than the hard cap into the inbox (the rest is lost to SYS_LOSS, once).
/// </summary>
[Trait("Category", "requires-mysql")]
public class ItemDeliveryMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public ItemDeliveryMySqlTests(MySqlTestDatabase db) => _db = db;

    private sealed class On : IProductionSettingsReader
    {
        private readonly long _cap;

        public On(long cap) => _cap = cap;

        public Task<ProductionSettingsDto> GetAsync() => Task.FromResult(new ProductionSettingsDto
        {
            Enabled = true,
            Delivery = new ProductionDeliverySettingsDto { InboxBaseCapUnits = _cap },
        });
    }

    private async Task<(int User, int Blueprint)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var user = new User { Username = "p7_" + suffix };
        var blueprint = new ItemBlueprint { Name = "Oak " + suffix, DefaultDisplayName = "Oak" };
        ctx.Users.Add(user);
        ctx.ItemBlueprints.Add(blueprint);
        await ctx.SaveChangesAsync();
        return (user.Id, blueprint.Id);
    }

    private async Task<List<(ItemDeliveryDto? Result, Exception? Error)>> InParallelAsync(int count, long cap,
        Func<IItemDeliveryService, int, Task<ItemDeliveryDto>> call)
    {
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await start.Task;
            try
            {
                await using var ctx = _db.NewContext();
                var service = new ItemDeliveryService(ctx, new StockService(ctx), new On(cap));
                return ((ItemDeliveryDto?)await call(service, i), (Exception?)null);
            }
            catch (Exception ex)
            {
                return ((ItemDeliveryDto?)null, ex);
            }
        }).ToList();
        start.SetResult();
        return (await Task.WhenAll(tasks)).ToList();
    }

    private static ItemDeliveryRequestDto Request(int user, int blueprint, string key, long amount) => new()
    {
        UserId = user, Reason = ItemDeliveryReason.Lootbox, IdempotencyKey = key,
        Lines = new() { new ItemDeliveryLineRequestDto { ItemBlueprintId = blueprint, Amount = amount } },
    };

    [MySqlFact]
    public async Task ConcurrentFirstDeliveries_MakeOneInbox_AndEveryUnitArrives()
    {
        var (user, blueprint) = await SeedAsync();
        var results = await InParallelAsync(12, 10_000, (s, i) => s.DeliverAsync(Request(user, blueprint, $"first-{user}-{i}", 5),
            DeliveryActor.GameServer(user)));

        Assert.All(results, r => Assert.Null(r.Error));
        await using var ctx = _db.NewContext();
        var inbox = Assert.Single(ctx.Storages.Where(s => s.UserId == user && s.Purpose == StoragePurpose.Inbox));
        Assert.Equal(60, inbox.TotalUnits);
        Assert.Equal(12, ctx.Set<ItemDelivery>().Count(d => d.UserId == user));
    }

    [MySqlFact]
    public async Task OneKey_DeliversOnce_UnderConcurrency()
    {
        var (user, blueprint) = await SeedAsync();
        var key = $"same-{user}";
        var results = await InParallelAsync(8, 10_000, (s, _) => s.DeliverAsync(Request(user, blueprint, key, 7), DeliveryActor.GameServer(user)));

        Assert.All(results, r => Assert.Null(r.Error));
        Assert.Single(results.Select(r => r.Result!.Id).Distinct());
        await using var ctx = _db.NewContext();
        Assert.Equal(7, ctx.Storages.Single(s => s.UserId == user && s.Purpose == StoragePurpose.Inbox).TotalUnits);
        Assert.Single(ctx.Set<ItemDelivery>().Where(d => d.IdempotencyKey == key));
    }

    [MySqlFact]
    public async Task ConcurrentDeliveries_NeverPassTheCap_AndTheRestIsLostOnce()
    {
        var (user, blueprint) = await SeedAsync();
        var results = await InParallelAsync(10, 100, (s, i) => s.DeliverAsync(Request(user, blueprint, $"cap-{user}-{i}", 20),
            DeliveryActor.GameServer(user)));

        Assert.All(results, r => Assert.Null(r.Error));
        Assert.Equal(100, results.Sum(r => r.Result!.InboxedUnits));
        Assert.Equal(100, results.Sum(r => r.Result!.LostUnits));
        await using var ctx = _db.NewContext();
        Assert.Equal(100, ctx.Storages.Single(s => s.UserId == user && s.Purpose == StoragePurpose.Inbox).TotalUnits);
        var lossId = ctx.Storages.Single(s => s.SystemKey == Storage.SysLoss).Id;
        Assert.Equal(100, ctx.StockMoves.Where(m => m.ToStorageId == lossId && m.ItemBlueprintId == blueprint
            && m.Reason == StockReason.InboxOverflow).Sum(m => m.Amount));
    }
}
