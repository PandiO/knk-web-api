using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Package C rank seeds on a real MySQL (the recursive parent-chain check runs on the server): on a database made by
/// migrations only, Moderator and Admin get their tier's nodes, a node an ancestor or wildcard already decides is
/// skipped, running Up twice adds nothing, and Down takes back only the new grants.
/// </summary>
[Trait("Category", "requires-mysql")]
public class SeedCommandPermissionNodesMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private const string Previous = "20261024224826_AddSiegeSkipSeconds";
    private readonly MySqlTestDatabase _db;

    public SeedCommandPermissionNodesMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static async Task<HashSet<string>> NodesOf(knkwebapi_v2.Properties.KnKDbContext ctx, string group)
    {
        var id = await ctx.PermissionGroups.AsNoTracking().Where(g => g.Name == group).Select(g => g.Id).SingleAsync();
        return (await ctx.PermissionGrants.AsNoTracking().Where(g => g.HolderId == id).Select(g => g.Node).ToListAsync()).ToHashSet();
    }

    [MySqlFact]
    public async Task SeedsEveryStaffGroup_SkipsWhatTheChainDecides_AndDownTakesBackOnlyItsOwn()
    {
        await using (var ctx = _db.NewContext())
        {
            var moderator = await NodesOf(ctx, "Moderator");
            var admin = await NodesOf(ctx, "Admin");
            Assert.Contains("knk.region.bypass.containers", moderator);
            Assert.Contains("knk.tasks", moderator); // no Staff group here, so the Staff tier lands on Moderator
            Assert.Contains("knk.gamemode.*", admin);
            Assert.Contains("knk.siege.admin.join", admin);
            Assert.DoesNotContain("knk.siege.admin.join", moderator);
            Assert.Contains("knk.siege.play", await NodesOf(ctx, "Default"));

            // Make Admin inherit from Moderator and hold knk.admin.*, as on the alpha, then re-run from the previous step.
            var migrator = ctx.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            Assert.DoesNotContain("knk.gamemode.*", await NodesOf(ctx, "Admin"));
            Assert.Contains("knk.siege.play", await NodesOf(ctx, "Default")); // backfill stays
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE permission_groups a JOIN permission_groups m ON m.Name = 'Moderator' SET a.ParentGroupId = m.Id WHERE a.Name = 'Admin';");
            await ctx.Database.ExecuteSqlRawAsync(
                "INSERT INTO permission_grants (HolderId, Node, Value, ExpiresAt) SELECT Id, 'knk.admin.*', 1, NULL FROM permission_groups WHERE Name = 'Admin';");
            await migrator.MigrateAsync();
        }

        await using var check = _db.NewContext();
        var adminNodes = await NodesOf(check, "Admin");
        Assert.DoesNotContain("knk.region.bypass.containers", adminNodes); // inherited from Moderator
        Assert.DoesNotContain("knk.admin.item", adminNodes);               // covered by knk.admin.*
        Assert.Contains("knk.gamemode.*", adminNodes);
        Assert.Contains("knk.region.bypass.containers", await NodesOf(check, "Moderator"));
    }
}
