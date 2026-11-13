using knkwebapi_v2.Attributes;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Staff may freeze and unfreeze (PandiO/knk-workspace#367, developer 2026-11-13): the Staff group gets the two nodes
/// the freeze endpoints check, nothing already decided is overridden, and Down removes nothing. Data only.
/// </summary>
public class SeedStaffFreezeNodesTests
{
    private static IReadOnlyList<string> Up() => new SeedStaffFreezeNodes().UpOperations.Cast<SqlOperation>().Select(o => o.Sql).ToList();

    [Fact]
    public void TheNodesAreTheOnesTheFreezeEndpointsCheck()
    {
        Assert.Equal(new[] { StaffPermissions.Freeze, StaffPermissions.Unfreeze }, SeedStaffFreezeNodes.Nodes);
        Assert.Equal(new[] { "knk.freeze", "knk.unfreeze" }, SeedStaffFreezeNodes.Nodes); // the plugin's /freeze nodes (plugin.yml)
    }

    [Fact]
    public void Up_IsDataOnly_OneGrantPerNode_OnTheStaffGroupOnly()
    {
        Assert.All(new SeedStaffFreezeNodes().UpOperations, op => Assert.IsType<SqlOperation>(op));
        Assert.Equal(2, Up().Count);
        foreach (var node in SeedStaffFreezeNodes.Nodes)
        {
            Assert.Single(Up(), sql => sql.Contains($"SELECT pg.Id, '{node}', 1, NULL"));
        }
        Assert.All(Up(), sql => Assert.Contains("WHERE pg.Name = 'Staff' ", sql));
        Assert.DoesNotContain(Up(), sql => sql.Contains("'Moderator'") || sql.Contains("'Admin'"));
    }

    [Fact]
    public void Up_SkipsWhatTheGroupsChainAlreadyDecides()
    {
        Assert.All(Up(), sql => Assert.Contains("WITH RECURSIVE anc", sql));
        Assert.All(Up(), sql => Assert.Contains("NOT EXISTS", sql));
        // The same check as AM1's seed: the node itself, a trailing-* wildcard, and x.* for the bare node x.
        Assert.Contains(SeedDashboardActionNodes.Decides("knk.freeze"), Up()[0]);
        Assert.Contains(SeedDashboardActionNodes.Decides("knk.unfreeze"), Up()[1]);
    }

    [Fact]
    public void Down_RemovesNothing()
    {
        Assert.Empty(new SeedStaffFreezeNodes().DownOperations);
    }

    [Fact]
    public void RunsAfterTheLatestMigrationOnNext()
    {
        Assert.True(string.CompareOrdinal("20261113161725_SeedStaffFreezeNodes", "20261109101501_AddConfigurationDrafts") > 0);
    }
}
