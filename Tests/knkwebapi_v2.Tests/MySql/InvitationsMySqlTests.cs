using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// KNG-212 on a real MySQL: the conditional UPDATE that takes an invitation's use (EF InMemory runs it
/// single-threaded, so only MySQL shows that two sign-ups can't share the last use).
/// </summary>
[Trait("Category", "requires-mysql")]
public class InvitationUseMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public InvitationUseMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private async Task<int> SeedAsync(int maxUses, int useCount)
    {
        await using var ctx = _db.NewContext();
        var invitation = new Invitation
        {
            Kind = InvitationKind.Shared,
            CodeHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            CodeHint = "ABCD",
            Label = "test",
            MaxUses = maxUses,
            UseCount = useCount,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        ctx.Invitations.Add(invitation);
        await ctx.SaveChangesAsync();
        return invitation.Id;
    }

    [MySqlFact]
    public async Task TwoConcurrentRedemptionsOfTheLastUse_ExactlyOneWins()
    {
        for (var round = 0; round < 10; round++)
        {
            var id = await SeedAsync(maxUses: 3, useCount: 2);
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var ctx = _db.NewContext();
                return await new InvitationRepository(ctx).TryTakeUseAsync(id, DateTime.UtcNow);
            }));

            Assert.Equal(1, results.Count(r => r));
            await using var check = _db.NewContext();
            Assert.Equal(3, (await check.Invitations.SingleAsync(i => i.Id == id)).UseCount);
        }
    }

    [MySqlFact]
    public async Task PausedRevokedAndExpiredInvites_GiveNoUse()
    {
        var paused = await SeedAsync(5, 0);
        var revoked = await SeedAsync(5, 0);
        var expired = await SeedAsync(5, 0);
        await using (var ctx = _db.NewContext())
        {
            (await ctx.Invitations.SingleAsync(i => i.Id == paused)).PausedAt = DateTime.UtcNow;
            (await ctx.Invitations.SingleAsync(i => i.Id == revoked)).RevokedAt = DateTime.UtcNow;
            (await ctx.Invitations.SingleAsync(i => i.Id == expired)).ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }

        await using var use = _db.NewContext();
        var repo = new InvitationRepository(use);
        Assert.False(await repo.TryTakeUseAsync(paused, DateTime.UtcNow));
        Assert.False(await repo.TryTakeUseAsync(revoked, DateTime.UtcNow));
        Assert.False(await repo.TryTakeUseAsync(expired, DateTime.UtcNow));
    }
}

/// <summary>
/// The grandfathering backfill of AddInvitations on real-shaped rows (decision I3, the chain's riskiest
/// step): the database is taken back to the migration before, filled with the kinds of accounts the
/// alpha has, and migrated forward again. Every active, non-deleted account must come out admitted.
/// </summary>
[Trait("Category", "requires-mysql")]
public class AddInvitationsBackfillMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private const string Previous = "20261021093950_AddEmailOutbox";
    private readonly MySqlTestDatabase _db;

    public AddInvitationsBackfillMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    [MySqlFact]
    public async Task EveryActiveAccount_IsAdmittedAsOfItsCreation()
    {
        await using (var ctx = _db.NewContext())
        {
            await ctx.GetService<IMigrator>().MigrateAsync(Previous);
            // Raw SQL: the model already has the new columns, the table doesn't.
            await ctx.Database.ExecuteSqlRawAsync(@"
INSERT INTO permission_holders (Id) VALUES (9001), (9002), (9003), (9004), (9005);
INSERT INTO users (Id, Username, Uuid, Email, PasswordHash, CreatedAt, EmailVerified, AccountCreatedVia, GatePassThroughMethodDefault,
    ActiveMode, PersonalSalaryMultiplier, PersonalGemBonusMultiplier, PersonalExpBonusMultiplier, LastSalaryPayoutAt, IsOnline,
    IsActive, DeletedAt, IsFrozen, TokenVersion)
VALUES
 (9001, 'MinecraftOnly', 'uuid-9001', NULL, NULL, '2026-09-01 10:00:00', 0, 1, 0, 0, 1, 1, 1, '2026-09-01', 0, 1, NULL, 0, 0),
 (9002, 'WithWebLogin', 'uuid-9002', 'web@example.com', 'hash', '2026-09-02 10:00:00', 0, 1, 0, 0, 1, 1, 1, '2026-09-02', 0, 1, NULL, 0, 0),
 (9003, 'webonly-legacy', NULL, 'legacy@example.com', 'hash', '2026-09-03 10:00:00', 0, 0, 0, 0, 1, 1, 1, '2026-09-03', 0, 1, NULL, 0, 0),
 (9004, 'Deactivated', 'uuid-9004', NULL, NULL, '2026-09-04 10:00:00', 0, 1, 0, 0, 1, 1, 1, '2026-09-04', 0, 0, NULL, 0, 0),
 (9005, 'Deleted', 'uuid-9005', NULL, NULL, '2026-09-05 10:00:00', 0, 1, 0, 0, 1, 1, 1, '2026-09-05', 0, 0, '2026-10-01', 0, 0);");
            await ctx.Database.MigrateAsync();
        }

        await using var check = _db.NewContext();
        var users = await check.Users.Where(u => u.Id >= 9001 && u.Id <= 9005).OrderBy(u => u.Id).ToListAsync();
        Assert.Equal(new DateTime(2026, 9, 1, 10, 0, 0), users[0].AdmittedAt);
        Assert.Equal(new DateTime(2026, 9, 2, 10, 0, 0), users[1].AdmittedAt);
        Assert.Equal(new DateTime(2026, 9, 3, 10, 0, 0), users[2].AdmittedAt);
        Assert.Null(users[3].AdmittedAt); // inactive: staff Admit (KNG-214) if it comes back
        Assert.Null(users[4].AdmittedAt); // soft-deleted
        Assert.All(users, u => Assert.Null(u.EmailVerifiedAt));
        Assert.All(users, u => Assert.Null(u.InvitedViaInvitationId));
    }
}
