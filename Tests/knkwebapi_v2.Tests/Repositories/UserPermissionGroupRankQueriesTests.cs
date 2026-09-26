using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Repositories;

/// <summary>The rank-expiry sweep's two queries (RankExpirySweepService, RANK_DISPLAY.md).</summary>
public class UserPermissionGroupRankQueriesTests
{
    private readonly string _dbName = $"RankQueries_{Guid.NewGuid()}";
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private async Task SeedAsync()
    {
        await using var db = NewContext();
        var defaultGroup = new PermissionGroup { Id = 1, Name = "Default", Weight = 0 };
        var royal = new PermissionGroup { Id = 5, Name = "Royal", Weight = 20, IsPremiumTier = true };
        var staff = new PermissionGroup { Id = 9, Name = "Staff", Weight = 100 };
        db.PermissionGroups.AddRange(defaultGroup, royal, staff);
        db.Users.AddRange(
            new User { Id = 10, Username = "expiredJustNow" },   // temp Royal expired 10s ago, nothing else
            new User { Id = 11, Username = "expiredButDefault" }, // temp Royal expired, still has Default
            new User { Id = 12, Username = "expiredLongAgo" },    // temp Royal expired last week, nothing else
            new User { Id = 13, Username = "activeRoyal" },
            new User { Id = 14, Username = "staffOnly" });        // expired Staff is not a rank
        db.UserPermissionGroups.AddRange(
            new UserPermissionGroup { UserId = 10, PermissionGroupId = 5, ExpiresAt = Now.AddSeconds(-10) },
            new UserPermissionGroup { UserId = 11, PermissionGroupId = 5, ExpiresAt = Now.AddSeconds(-10) },
            new UserPermissionGroup { UserId = 11, PermissionGroupId = 1, ExpiresAt = null },
            new UserPermissionGroup { UserId = 12, PermissionGroupId = 5, ExpiresAt = Now.AddDays(-7) },
            new UserPermissionGroup { UserId = 13, PermissionGroupId = 5, ExpiresAt = Now.AddDays(1) },
            new UserPermissionGroup { UserId = 14, PermissionGroupId = 9, ExpiresAt = Now.AddSeconds(-10) });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RanksExpiredBetween_OnlyRankMembershipsInTheWindow()
    {
        await SeedAsync();
        await using var db = NewContext();

        var expired = await new UserPermissionGroupRepository(db).GetRanksExpiredBetweenAsync(Now.AddSeconds(-30), Now, "Default");

        Assert.Equal(new[] { 10, 11 }, expired.Select(m => m.UserId).OrderBy(id => id).ToArray());
        Assert.All(expired, m => Assert.NotNull(m.User));
    }

    [Fact]
    public async Task UsersLeftWithoutRank_ExpiredRankAndNoActiveRank_AnyTimeAgo()
    {
        await SeedAsync();
        await using var db = NewContext();

        var users = await new UserPermissionGroupRepository(db).GetUsersLeftWithoutRankAsync(Now, "Default");

        Assert.Equal(new[] { 10, 12 }, users.Select(u => u.Id).OrderBy(id => id).ToArray());
    }
}
