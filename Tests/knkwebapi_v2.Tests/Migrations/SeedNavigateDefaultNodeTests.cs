using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// <c>knk.navigate</c> becomes <c>default: false</c> in the plugin's plugin.yml (PandiO/knk-workspace#845, option A),
/// so the Default group, which every account holds, gets the node from this seed instead. Nothing already decided is
/// overridden (an in-house deny stays a deny), <c>knk.navigate.any</c> is not seeded (the staff ranks are still open),
/// and Down removes nothing. Data only.
/// </summary>
public class SeedNavigateDefaultNodeTests
{
    private static IReadOnlyList<string> Up() => new SeedNavigateDefaultNode().UpOperations.Cast<SqlOperation>().Select(o => o.Sql).ToList();

    [Fact]
    public void TheNodeIsTheOnePluginYmlDeclaresForNavigate()
    {
        Assert.Equal("knk.navigate", SeedNavigateDefaultNode.Navigate);
        Assert.Equal("Default", SeedNavigateDefaultNode.DefaultGroup);
    }

    [Fact]
    public void Up_IsDataOnly_OneGrant_OnTheDefaultGroupOnly()
    {
        Assert.All(new SeedNavigateDefaultNode().UpOperations, op => Assert.IsType<SqlOperation>(op));
        var sql = Assert.Single(Up());
        Assert.Contains("INSERT INTO permission_grants (HolderId, Node, Value, ExpiresAt)", sql);
        Assert.Contains("SELECT pg.Id, 'knk.navigate', 1, NULL", sql);
        Assert.Contains("WHERE pg.Name = 'Default' ", sql);
        Assert.DoesNotContain("'Staff'", sql);
        Assert.DoesNotContain("'Moderator'", sql);
        Assert.DoesNotContain("'Admin'", sql);
    }

    [Fact]
    public void Up_DoesNotSeedTheBypassNode()
    {
        // knk.navigate.any waits for the developer to name the staff rank(s) (#845, open question on #25).
        Assert.DoesNotContain(Up(), sql => sql.Contains("knk.navigate.any"));
    }

    [Fact]
    public void Up_SkipsWhatTheGroupsChainAlreadyDecides_SoAnInHouseDenyStays()
    {
        var sql = Assert.Single(Up());
        Assert.Contains("WITH RECURSIVE anc", sql);
        Assert.Contains("NOT EXISTS", sql);
        // The node itself (granted or denied), a trailing-* wildcard such as knk.*, and knk.navigate.* for the bare node.
        Assert.Contains(SeedDashboardActionNodes.Decides("knk.navigate"), sql);
        Assert.Equal(SeedStaffFreezeNodes.GrantSql("Default", "knk.navigate"), sql);
    }

    [Fact]
    public void Down_RemovesNothing()
    {
        Assert.Empty(new SeedNavigateDefaultNode().DownOperations);
    }
}
