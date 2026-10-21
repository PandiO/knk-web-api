using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// KNG-218 on a real MySQL: SeedAlphaTesterGroup makes one "Alpha Tester" under Default, leaves a group of that
/// name alone when one exists (dev databases, link 3's tests), and its Down keeps a group that is in use.
/// </summary>
[Trait("Category", "requires-mysql")]
public class SeedAlphaTesterGroupMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private const string Previous = "20261021120500_SeedInvitationStaffNode";
    private readonly MySqlTestDatabase _db;

    public SeedAlphaTesterGroupMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    [MySqlFact]
    public async Task SeedsOneGroupUnderDefault_AndSkipsAnExistingOne()
    {
        await using (var ctx = _db.NewContext())
        {
            var group = await ctx.PermissionGroups.AsNoTracking().SingleAsync(g => g.Name == "Alpha Tester");
            var defaultGroup = await ctx.PermissionGroups.AsNoTracking().SingleAsync(g => g.Name == "Default");
            Assert.Equal(1, group.Weight);
            Assert.Equal(defaultGroup.Id, group.ParentGroupId);
            Assert.False(group.IsPremiumTier);
            Assert.True(await ctx.PermissionHolders.AnyAsync(h => h.Id == group.Id));

            // Down removes the unused group; a hand-made one with another weight then survives the Up.
            await ctx.GetService<IMigrator>().MigrateAsync(Previous);
            Assert.False(await ctx.PermissionGroups.AnyAsync(g => g.Name == "Alpha Tester"));
            await ctx.Database.ExecuteSqlRawAsync(
                "INSERT INTO permission_holders (ChatPrefix, ChatSuffix) VALUES (NULL, NULL); " +
                "INSERT INTO permission_groups (Id, Name, Weight, IsPremiumTier, SalaryMultiplier, ParentGroupId) " +
                "VALUES (LAST_INSERT_ID(), 'Alpha Tester', 7, 0, 1.0, NULL);");
            await ctx.Database.MigrateAsync();
        }

        await using var check = _db.NewContext();
        var groups = await check.PermissionGroups.AsNoTracking().Where(g => g.Name == "Alpha Tester").ToListAsync();
        Assert.Equal(7, Assert.Single(groups).Weight);
    }
}
