using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Overview map L1 (PandiO/knk-workspace#735, charter § 3.3): the outline migration is additive only, one new table
/// with a unique (world, region) index; its only FK is to domains with SET NULL, so a domain delete never fails on it.
/// </summary>
public class AddMapRegionOutlinesTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddMapRegionOutlines().UpOperations;

    [Fact]
    public void Up_OnlyCreatesTheOutlineTableAndItsIndexes()
    {
        var table = Assert.Single(Up().OfType<CreateTableOperation>());
        Assert.Equal("map_region_outlines", table.Name);
        Assert.All(Up(), op => Assert.True(op is CreateTableOperation or CreateIndexOperation, op.GetType().Name));
        Assert.Equal(new[] { "DomainId", "Id", "MaxY", "MinY", "PointsJson", "UpdatedAt", "WgRegionId", "WorldName" },
            table.Columns.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Up_OneRowPerWorldAndRegion()
    {
        var unique = Assert.Single(Up().OfType<CreateIndexOperation>(), i => i.IsUnique);
        Assert.Equal(new[] { "WorldName", "WgRegionId" }, unique.Columns);
    }

    [Fact]
    public void Up_DomainLinkIsClearedWhenTheDomainIsDeleted()
    {
        var fk = Assert.Single(Up().OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys));
        Assert.Equal(("domains", ReferentialAction.SetNull), (fk.PrincipalTable, fk.OnDelete));
    }

    [Fact]
    public void Down_DropsOnlyTheTable()
    {
        var drop = Assert.IsType<DropTableOperation>(Assert.Single(new AddMapRegionOutlines().DownOperations));
        Assert.Equal("map_region_outlines", drop.Name);
    }
}
