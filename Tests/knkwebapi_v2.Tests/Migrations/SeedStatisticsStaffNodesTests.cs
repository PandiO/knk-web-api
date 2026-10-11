using knkwebapi_v2.Attributes;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-34 staff nodes for the KNG-80 staff groups (developer decision 2026-10-10): data only; Moderator
/// gets the statistics view, Admin also the data-deletion request; owner nodes are never seeded; it
/// runs after the migration that creates the groups.
/// </summary>
public class SeedStatisticsStaffNodesTests
{
    private static IReadOnlyList<SqlOperation> Up() => new SeedStatisticsStaffNodes().UpOperations.Cast<SqlOperation>().ToList();

    [Fact]
    public void Up_GrantsTheStatisticsNodes_ToModeratorAndAdmin_Idempotently()
    {
        Assert.All(new SeedStatisticsStaffNodes().UpOperations, op => Assert.IsType<SqlOperation>(op));
        Assert.Equal(new[] { StaffPermissions.ViewStatistics }, SeedStatisticsStaffNodes.ModeratorNodes);
        Assert.Equal(new[] { StaffPermissions.ViewStatistics, StaffPermissions.RequestDataDeletion }, SeedStatisticsStaffNodes.AdminNodes);
        Assert.Equal(3, Up().Count);
        Assert.All(Up(), op => Assert.Contains("AND NOT EXISTS (SELECT 1 FROM permission_grants g", op.Sql));
        Assert.Contains(Up(), op => op.Sql.Contains("pg.Name = 'Moderator'") && op.Sql.Contains("'knk.admin.statistics.view'"));
        Assert.Contains(Up(), op => op.Sql.Contains("pg.Name = 'Admin'") && op.Sql.Contains("'knk.admin.privacy.request'"));
        Assert.DoesNotContain(Up(), op => op.Sql.Contains("knk.owner"));
    }

    [Fact]
    public void RunsAfterTheMigrationThatCreatesTheGroups()
    {
        Assert.True(string.CompareOrdinal("20261010170000_SeedStatisticsStaffNodes", "20261009091416_SeedLocationRetentionStaffGroups") > 0);
        Assert.Equal(3, new SeedStatisticsStaffNodes().DownOperations.Count);
    }
}
