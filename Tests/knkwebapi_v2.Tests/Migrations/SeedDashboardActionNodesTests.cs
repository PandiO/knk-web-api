using knkwebapi_v2.Attributes;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Dashboard access model AM1 (PandiO/knk-workspace#280, DESIGN.md § 4.5): Admin gets the three action nodes; every
/// explicit area-node holder but the Staff group gets delete and export; nothing decided already is overridden. Data only.
/// </summary>
public class SeedDashboardActionNodesTests
{
    private static IReadOnlyList<string> Up() => new SeedDashboardActionNodes().UpOperations.Cast<SqlOperation>().Select(o => o.Sql).ToList();

    private static IReadOnlyList<string> Down() => new SeedDashboardActionNodes().DownOperations.Cast<SqlOperation>().Select(o => o.Sql).ToList();

    [Fact]
    public void Up_IsDataOnly_ThreeAdminGrants_AndTwoBackfillsPerNode()
    {
        Assert.All(new SeedDashboardActionNodes().UpOperations, op => Assert.IsType<SqlOperation>(op));
        Assert.All(new SeedDashboardActionNodes().DownOperations, op => Assert.IsType<SqlOperation>(op));
        Assert.Equal(3 + 2 * 2, Up().Count);
        Assert.All(Up(), sql => Assert.Contains("WITH RECURSIVE anc", sql));
    }

    [Fact]
    public void TheNodesAreTheStaffPermissionsConstants()
    {
        Assert.Equal(new[] { StaffPermissions.Delete, StaffPermissions.DeleteBulk, StaffPermissions.Export }, SeedDashboardActionNodes.AdminNodes);
        Assert.Equal(new[] { StaffPermissions.Delete, StaffPermissions.Export }, SeedDashboardActionNodes.BackfillNodes);
        // Every area node of the registry is backfilled, plus the group write node.
        Assert.Equal(DashboardAccess.AreaNodes.Append(StaffPermissions.UserPermissions).OrderBy(n => n),
            SeedDashboardActionNodes.AreaNodes.OrderBy(n => n));
    }

    [Fact]
    public void BulkDelete_IsNeverBackfilled_OnlyGivenToAdmin()
    {
        var bulk = Up().Where(sql => sql.Contains("'knk.admin.delete.bulk'")).ToList();
        var only = Assert.Single(bulk);
        Assert.Contains("pg.Name = 'Admin'", only);
    }

    [Fact]
    public void TheBackfill_SkipsTheStaffGroup()
    {
        var groupBackfills = Up().Where(sql => sql.Contains("pg.Name <> 'Staff'")).ToList();
        Assert.Equal(2, groupBackfills.Count);
        Assert.All(Down().Where(sql => !sql.Contains("pg.Name = 'Admin'")), sql => Assert.Contains("s.Name = 'Staff'", sql));
    }

    [Fact]
    public void EveryGrant_SkipsAHolderWhoseChainAlreadyDecidesTheNode_IncludingTheBareNodeOfAWildcard()
    {
        Assert.All(Up(), sql => Assert.Contains("AND NOT EXISTS (SELECT 1 FROM", sql));
        var decides = SeedDashboardActionNodes.Decides("knk.admin.delete");
        Assert.Contains("g.Node = 'knk.admin.delete'", decides);
        Assert.Contains("LEFT(g.Node, CHAR_LENGTH(g.Node) - 2) = 'knk.admin.delete'", decides); // knk.admin.delete.*
        // Users: their own grants and every active group with its parents.
        Assert.Contains("user_permission_groups m", SeedDashboardActionNodes.UserBackfillSql("knk.admin.delete"));
        Assert.Contains("m.ExpiresAt IS NULL OR m.ExpiresAt > UTC_TIMESTAMP()", SeedDashboardActionNodes.UserBackfillSql("knk.admin.delete"));
    }

    [Fact]
    public void OnlyActiveExplicitAreaGrants_Count()
    {
        var sql = SeedDashboardActionNodes.GroupBackfillSql("knk.admin.export");
        Assert.Contains("e.Value = 1 AND (e.ExpiresAt IS NULL OR e.ExpiresAt > UTC_TIMESTAMP()) AND e.Node IN ('knk.admin.content', 'knk.admin.world'", sql);
    }

    [Fact]
    public void Down_RemovesOnlyGrantsWithoutExpiry_NeverADeny()
    {
        Assert.Equal(2 + 3, Down().Count);
        Assert.All(Down(), sql => Assert.Contains("Value = 1 AND", sql));
        Assert.All(Down(), sql => Assert.Contains("ExpiresAt IS NULL", sql));
    }

    [Fact]
    public void RunsAfterTheLatestMigrationOfItsBase()
    {
        Assert.True(string.CompareOrdinal("20261108181026_SeedDashboardActionNodes", "20261108162526_AddKitClaimDelivery") > 0);
    }
}
