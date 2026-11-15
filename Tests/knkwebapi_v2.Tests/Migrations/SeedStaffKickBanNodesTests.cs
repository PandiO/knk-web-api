using knkwebapi_v2.Attributes;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Staff may kick and ban from the web app (PandiO/knk-workspace#565, developer 2026-11-15): the Staff group gets the
/// two nodes the kick and ban endpoints check, nothing already decided is overridden, and Down removes nothing. Data
/// only, built like SeedStaffFreezeNodes.
/// </summary>
public class SeedStaffKickBanNodesTests
{
    private static IReadOnlyList<string> Up() => new SeedStaffKickBanNodes().UpOperations.Cast<SqlOperation>().Select(o => o.Sql).ToList();

    [Fact]
    public void TheNodesAreTheOnesTheKickAndBanEndpointsCheck()
    {
        Assert.Equal(new[] { StaffPermissions.Kick, StaffPermissions.Ban }, SeedStaffKickBanNodes.Nodes);
        Assert.Equal(new[] { "knk.kick", "knk.ban" }, SeedStaffKickBanNodes.Nodes);
    }

    [Fact]
    public void Up_IsDataOnly_OneGrantPerNode_OnTheStaffGroupOnly()
    {
        Assert.All(new SeedStaffKickBanNodes().UpOperations, op => Assert.IsType<SqlOperation>(op));
        Assert.Equal(2, Up().Count);
        foreach (var node in SeedStaffKickBanNodes.Nodes)
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
        Assert.Contains(SeedDashboardActionNodes.Decides("knk.kick"), Up()[0]);
        Assert.Contains(SeedDashboardActionNodes.Decides("knk.ban"), Up()[1]);
    }

    [Fact]
    public void Down_RemovesNothing()
    {
        Assert.Empty(new SeedStaffKickBanNodes().DownOperations);
    }

    [Fact]
    public void RunsAfterTheLatestMigrationOnNext()
    {
        Assert.True(string.CompareOrdinal("20261115125459_SeedStaffKickBanNodes", "20261115092735_AddAuditLogSubject") > 0);
    }
}
