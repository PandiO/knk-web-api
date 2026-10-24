using knkwebapi_v2.Configuration;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-218: the "Alpha Tester" group that invitations grant by default (decision I4) is seeded once, data
/// only: weight 1, parent Default, no nodes, not a premium tier; skipped when a group of that name exists.
/// Down removes it only while nothing uses it.
/// </summary>
public class SeedAlphaTesterGroupTests
{
    private static IReadOnlyList<SqlOperation> Up() => new SeedAlphaTesterGroup().UpOperations.Cast<SqlOperation>().ToList();

    private static IReadOnlyList<SqlOperation> Down() => new SeedAlphaTesterGroup().DownOperations.Cast<SqlOperation>().ToList();

    [Fact]
    public void Up_IsDataOnly_AndIdempotent()
    {
        Assert.All(new SeedAlphaTesterGroup().UpOperations, op => Assert.IsType<SqlOperation>(op));
        Assert.Equal(2, Up().Count);
        Assert.All(Up(), op => Assert.Contains("WHERE NOT EXISTS (SELECT 1 FROM permission_groups WHERE Name = 'Alpha Tester')", op.Sql));
        Assert.StartsWith("INSERT INTO permission_holders", Up()[0].Sql);
        Assert.StartsWith("INSERT INTO permission_groups", Up()[1].Sql);
    }

    [Fact]
    public void Up_MakesALowNonPremiumGroup_UnderDefault_WithoutNodes()
    {
        var insert = Up()[1].Sql;

        Assert.Contains("LAST_INSERT_ID(), 'Alpha Tester', 1, 0, 1.0,", insert);
        Assert.Contains("d.Name = 'Default' AND d.IsPremiumTier = 0", insert);
        Assert.DoesNotContain(Up(), op => op.Sql.Contains("permission_grants"));
        Assert.Equal(1, SeedAlphaTesterGroup.Weight);
    }

    [Fact]
    public void IsTheGroupInvitationsGrantByDefault()
    {
        Assert.Equal(new[] { SeedAlphaTesterGroup.Group }, new InvitationSettings().InvitableGroups);
    }

    [Fact]
    public void Down_RemovesOnlyAnUnusedGroup()
    {
        var delete = Assert.Single(Down()).Sql;

        Assert.Contains("pg.Name = 'Alpha Tester'", delete);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM user_permission_groups", delete);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM permission_grants", delete);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM invitations i WHERE i.GrantGroupId = pg.Id)", delete);
    }

    [Fact]
    public void RunsAfterDefaultAndTheInvitationsTables()
    {
        const string self = "20261021174225_SeedAlphaTesterGroup";
        Assert.True(string.CompareOrdinal(self, "20260925072056_SeedDefaultPermissionGroup") > 0);
        Assert.True(string.CompareOrdinal(self, "20261021120136_AddInvitations") > 0);
        Assert.True(string.CompareOrdinal(self, "20261021120500_SeedInvitationStaffNode") > 0);
    }
}
