using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Landmark-domain A02 (LD10, LD11, LD16, PandiO/knk-workspace#983, charter § 3.3): the one migration of the chain is
/// not additive. It drops the landmark rows before anything else, rebuilds <c>landmarks</c> as the TPT child of
/// <c>domains</c> (keyed by the domain id, the parent SET NULL) and makes <c>domains.WgRegionId</c> nullable. Down first
/// removes the landmark domains, so the #708 table comes back empty and no domain is left without a region. The
/// Migrations workflow applies Up and Down on a fresh MySQL database.
/// </summary>
public class LandmarkAsDomainTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new LandmarkAsDomain().UpOperations;
    private static IReadOnlyList<MigrationOperation> Down() => new LandmarkAsDomain().DownOperations;

    [Fact]
    public void Up_DropsTheLandmarkRowsFirst()
    {
        var first = Assert.IsType<SqlOperation>(Up()[0]);
        Assert.Equal("DELETE FROM `landmarks`;", first.Sql);
    }

    [Fact]
    public void Up_MovesTheDomainFieldsToDomains_AndKeepsTheLandmarkFields()
    {
        Assert.Equal(new[] { "CreatedAt", "LocationId", "Name", "Radius", "RegionId" },
            Up().OfType<DropColumnOperation>().Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
        var renames = Up().OfType<RenameColumnOperation>().ToDictionary(r => r.Name, r => r.NewName);
        Assert.Equal(("AreaKind", "ParentDomainId"), (renames["ExtentKind"], renames["DomainId"]));
        Assert.Equal("ShowEnterLeaveMessages", Assert.Single(Up().OfType<AddColumnOperation>()).Name);
        Assert.Equal("`Preference` >= 0.25 AND `Preference` <= 4", Assert.Single(Up().OfType<AddCheckConstraintOperation>()).Sql);
    }

    [Fact]
    public void Up_KeysLandmarksByTheDomainId_AndTheParentIsClearedOnDelete()
    {
        var fks = Up().OfType<AddForeignKeyOperation>().ToDictionary(f => f.Columns.Single());
        Assert.Equal(("domains", ReferentialAction.Cascade), (fks["Id"].PrincipalTable, fks["Id"].OnDelete));
        Assert.Equal(("domains", ReferentialAction.SetNull), (fks["ParentDomainId"].PrincipalTable, fks["ParentDomainId"].OnDelete));
        Assert.Contains(Up().OfType<DropForeignKeyOperation>(), f => f.Name == "FK_landmarks_locations_LocationId");
    }

    [Fact]
    public void Up_MakesTheDomainRegionNullable_AndChangesNoOtherDomainColumn()
    {
        var altered = Up().OfType<AlterColumnOperation>().ToList();
        var region = Assert.Single(altered, a => a.Table == "domains");
        Assert.Equal(("WgRegionId", true, false), (region.Name, region.IsNullable, region.OldColumn.IsNullable));
        Assert.All(Up().OfType<AddColumnOperation>(), c => Assert.Equal("landmarks", c.Table));
        Assert.All(Up().OfType<DropColumnOperation>(), c => Assert.Equal("landmarks", c.Table));
    }

    [Fact]
    public void Down_RemovesTheLandmarkDomainsFirst_ThenRestoresThe708Table()
    {
        var sql = Down().TakeWhile(o => o is SqlOperation).Cast<SqlOperation>().Select(o => o.Sql).ToList();
        Assert.Equal(4, sql.Count);
        Assert.Contains("DELETE FROM `landmarks`;", sql);
        Assert.Contains(sql, s => s.StartsWith("DELETE FROM `domains` WHERE `Id` IN", StringComparison.Ordinal));

        Assert.Contains(Down().OfType<AddForeignKeyOperation>(), f => f.PrincipalTable == "locations" && f.OnDelete == ReferentialAction.Restrict);
        var region = Assert.Single(Down().OfType<AlterColumnOperation>(), a => a.Table == "domains");
        Assert.False(region.IsNullable);
    }
}
