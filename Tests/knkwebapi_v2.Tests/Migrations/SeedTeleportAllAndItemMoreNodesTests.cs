using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Alpha test run 1, package I (PandiO/knk-workspace#171, #175): rank seeds for the teleport all forms (Moderator and
/// Admin, not general Staff) and /knk item more (Admin). Data only, the parent-chain check of package C's seed, and
/// Down removes only these grants.
/// </summary>
public class SeedTeleportAllAndItemMoreNodesTests
{
    private static IReadOnlyList<string> Up() => new SeedTeleportAllAndItemMoreNodes().UpOperations.Cast<SqlOperation>().Select(o => o.Sql).ToList();

    private static IReadOnlyList<string> Down() => new SeedTeleportAllAndItemMoreNodes().DownOperations.Cast<SqlOperation>().Select(o => o.Sql).ToList();

    [Fact]
    public void Up_IsDataOnly_AndEveryGrantChecksTheGroupsParentChain()
    {
        Assert.All(new SeedTeleportAllAndItemMoreNodes().UpOperations, op => Assert.IsType<SqlOperation>(op));
        Assert.All(Up(), sql => Assert.Contains("WITH RECURSIVE anc", sql));
        Assert.Equal(SeedTeleportAllAndItemMoreNodes.NewGrants.Sum(g => SeedCommandPermissionNodes.Tiers[g.Tier].Length), Up().Count);
    }

    [Theory]
    [InlineData("knk.teleport.all.hub")]
    [InlineData("knk.teleport.all.spawn")]
    [InlineData("knk.teleport.all.tp")]
    [InlineData("knk.teleport.all.tphere")]
    public void TheAllFormsGoToModeratorAndAdminButNotStaff(string node)
    {
        Assert.Contains((node, "Moderator"), SeedTeleportAllAndItemMoreNodes.NewGrants);
        Assert.DoesNotContain(Up(), sql => sql.Contains("pg.Name = 'Staff'") && sql.Contains($"'{node}'"));
        Assert.Contains(Up(), sql => sql.Contains("pg.Name = 'Admin'") && sql.Contains($"'{node}'"));
    }

    [Fact]
    public void ItemMoreIsAdminOnly_AndNoExemptionIsSeeded()
    {
        Assert.Contains(("knk.admin.item.more", "Admin"), SeedTeleportAllAndItemMoreNodes.NewGrants);
        Assert.DoesNotContain(Up(), sql => sql.Contains("knk.teleport.exempt"));
        Assert.DoesNotContain(Up(), sql => sql.Contains("'knk.admin.item.more'") && !sql.Contains("pg.Name = 'Admin'"));
    }

    [Fact]
    public void Down_RemovesOnlyItsOwnGrants()
    {
        Assert.Equal(Up().Count, Down().Count);
        Assert.All(Down(), sql => Assert.Contains("g.Value = 1 AND g.ExpiresAt IS NULL", sql));
    }

    [Fact]
    public void RunsAfterTheLatestMigrationOnMaster()
    {
        Assert.True(string.CompareOrdinal("20261027200000_SeedTeleportAllAndItemMoreNodes", "20261026160000_SeedCommandPermissionNodes") > 0);
    }
}
