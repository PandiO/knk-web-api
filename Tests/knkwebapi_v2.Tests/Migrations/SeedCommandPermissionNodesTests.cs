using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Alpha test run 1, package C (PandiO/knk-workspace#158, #157, #159): the rank seeds for the plugin's command nodes.
/// Data only; every grant skips a group that (or whose parent chain) already decides the node; Down removes only the
/// new grants, never the siege backfill the alpha seed already had.
/// </summary>
public class SeedCommandPermissionNodesTests
{
    private static IReadOnlyList<string> Up() => new SeedCommandPermissionNodes().UpOperations.Cast<SqlOperation>().Select(o => o.Sql).ToList();

    private static IReadOnlyList<string> Down() => new SeedCommandPermissionNodes().DownOperations.Cast<SqlOperation>().Select(o => o.Sql).ToList();

    [Fact]
    public void Up_IsDataOnly_AndEveryGrantChecksTheGroupsParentChain()
    {
        Assert.All(new SeedCommandPermissionNodes().UpOperations, op => Assert.IsType<SqlOperation>(op));
        Assert.All(Up(), sql => Assert.Contains("WITH RECURSIVE anc", sql));
        Assert.All(Up(), sql => Assert.Contains("AND NOT EXISTS (SELECT 1 FROM anc INNER JOIN permission_grants g", sql));
        var expected = SeedCommandPermissionNodes.NewGrants.Concat(SeedCommandPermissionNodes.BackfillGrants)
            .Sum(g => SeedCommandPermissionNodes.Tiers[g.Tier].Length);
        Assert.Equal(expected, Up().Count);
    }

    [Theory]
    [InlineData("knk.region.bypass.containers", "Moderator")] // #157: moderators and admins, not general staff
    [InlineData("knk.siege.admin.join", "Admin")]           // #158 comment: admins only
    [InlineData("knk.gamemode.*", "Admin")]
    [InlineData("knk.tasks", "Staff")]
    [InlineData("knk.notify.joinattempt", "Staff")]
    public void NodesGoToTheirTier(string node, string tier)
    {
        Assert.Contains((node, tier), SeedCommandPermissionNodes.NewGrants);
    }

    [Fact]
    public void TheContainerBypassNeverReachesTheGeneralStaffGroup()
    {
        Assert.DoesNotContain(Up(), sql => sql.Contains("pg.Name = 'Staff'") && sql.Contains("'knk.region.bypass.containers'"));
        Assert.Contains(Up(), sql => sql.Contains("pg.Name = 'Moderator'") && sql.Contains("'knk.region.bypass.containers'"));
        Assert.Contains(Up(), sql => sql.Contains("pg.Name = 'Admin'") && sql.Contains("'knk.region.bypass.containers'"));
    }

    [Fact]
    public void Down_RemovesOnlyTheNewGrants()
    {
        Assert.Equal(SeedCommandPermissionNodes.NewGrants.Sum(g => SeedCommandPermissionNodes.Tiers[g.Tier].Length), Down().Count);
        Assert.DoesNotContain(Down(), sql => sql.Contains("knk.siege.play") || sql.Contains("knk.siege.admin.control"));
        Assert.All(Down(), sql => Assert.Contains("g.Value = 1 AND g.ExpiresAt IS NULL", sql));
    }

    [Fact]
    public void RunsAfterTheLatestMigrationOnMaster()
    {
        Assert.True(string.CompareOrdinal("20261026160000_SeedCommandPermissionNodes", "20261024224826_AddSiegeSkipSeconds") > 0);
    }
}
