using Xunit;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// PandiO/knk-workspace#261 on a real MySQL: a web grant is a pending delivery until a game server
/// takes it with one conditional UPDATE. Concurrent takers (a realtime push, a poll and a join drain,
/// or two game servers), each in its own DbContext, get exactly one winner, so the items are placed
/// once; claims the game server placed itself are never listed or taken.
/// </summary>
[Trait("Category", "requires-mysql")]
public class KitGrantDeliveryMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    public KitGrantDeliveryMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private async Task<(int UserId, int Pending, int Placed)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var user = new User { Username = "kd" + Guid.NewGuid().ToString("N")[..10] };
        var kit = new Kit { Name = "Knight " + Guid.NewGuid().ToString("N")[..8] };
        ctx.Users.Add(user);
        ctx.Kits.Add(kit);
        await ctx.SaveChangesAsync();
        var pending = new KitClaim { KitId = kit.Id, UserId = user.Id, ClaimedAt = Now, DeliveryRequestedAt = Now };
        var placed = new KitClaim { KitId = kit.Id, UserId = user.Id, ClaimedAt = Now };
        ctx.KitClaims.AddRange(pending, placed);
        await ctx.SaveChangesAsync();
        return (user.Id, pending.Id, placed.Id);
    }

    [MySqlFact]
    public async Task OnlyPendingDeliveriesAreListed_WithTheirKit()
    {
        var (userId, pending, _) = await SeedAsync();

        await using var ctx = _db.NewContext();
        var listed = Assert.Single(await new KitRepository(ctx).GetPendingDeliveriesAsync(userId));
        Assert.Equal(pending, listed.Id);
        Assert.StartsWith("Knight", listed.Kit.Name);
    }

    [MySqlFact]
    public async Task ConcurrentTakers_HaveExactlyOneWinner()
    {
        var (userId, pending, _) = await SeedAsync();
        const int takers = 6;

        var contexts = Enumerable.Range(0, takers).Select(_ => _db.NewContext()).ToList();
        try
        {
            using var start = new ManualResetEventSlim(false);
            var calls = contexts.Select(ctx => Task.Run(async () =>
            {
                start.Wait();
                return await new KitRepository(ctx).TryMarkDeliveredAsync(pending, userId, Now);
            })).ToList();
            start.Set();
            var results = await Task.WhenAll(calls);

            Assert.Equal(1, results.Count(won => won));
        }
        finally
        {
            foreach (var ctx in contexts) await ctx.DisposeAsync();
        }

        await using var check = _db.NewContext();
        Assert.Empty(await new KitRepository(check).GetPendingDeliveriesAsync(userId));
        Assert.Equal(Now, (await new KitRepository(check).GetClaimAsync(pending))!.DeliveredAt);
    }

    [MySqlFact]
    public async Task APlacedClaim_OrSomeoneElsesClaim_IsNeverTaken()
    {
        var (userId, pending, placed) = await SeedAsync();

        await using var ctx = _db.NewContext();
        var repo = new KitRepository(ctx);
        Assert.False(await repo.TryMarkDeliveredAsync(placed, userId, Now));
        Assert.False(await repo.TryMarkDeliveredAsync(pending, userId + 100_000, Now));
        Assert.True(await repo.TryMarkDeliveredAsync(pending, userId, Now));
    }
}
