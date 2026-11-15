using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// PandiO/knk-workspace#409: DropTownStreet drops the TownStreet link table (a town's streets are its districts'
/// streets now). Down recreates it as InitialCreate made it and refills it from DistrictStreet and districts.
/// </summary>
public class DropTownStreetMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new DropTownStreet().UpOperations;
    private static IReadOnlyList<MigrationOperation> Down() => new DropTownStreet().DownOperations;

    [Fact]
    public void Up_DropsOnlyTheTownStreetTable()
    {
        var drop = Assert.IsType<DropTableOperation>(Assert.Single(Up()));
        Assert.Equal("TownStreet", drop.Name);
    }

    [Fact]
    public void Down_RecreatesTheTable_AsInitialCreateMadeIt()
    {
        var table = Assert.Single(Down().OfType<CreateTableOperation>());
        Assert.Equal("TownStreet", table.Name);
        Assert.Equal(new[] { "StreetId", "TownId" }, table.Columns.Select(c => c.Name));
        Assert.All(table.Columns, c => Assert.Equal(typeof(int), c.ClrType));
        Assert.All(table.Columns, c => Assert.False(c.IsNullable));

        Assert.Equal("PK_TownStreet", table.PrimaryKey!.Name);
        Assert.Equal(new[] { "StreetId", "TownId" }, table.PrimaryKey.Columns);

        var keys = table.ForeignKeys.OrderBy(f => f.Name).ToList();
        Assert.Equal(new[] { "FK_TownStreet_streets_StreetId", "FK_TownStreet_towns_TownId" }, keys.Select(f => f.Name));
        Assert.Equal(new[] { "streets", "towns" }, keys.Select(f => f.PrincipalTable));
        Assert.Equal(new[] { "StreetId", "TownId" }, keys.Select(f => f.Columns.Single()));
        Assert.All(keys, f => Assert.Equal(ReferentialAction.Cascade, f.OnDelete));

        var index = Assert.Single(Down().OfType<CreateIndexOperation>());
        Assert.Equal("IX_TownStreet_TownId", index.Name);
        Assert.Equal("TownStreet", index.Table);
        Assert.Equal(new[] { "TownId" }, index.Columns);
    }

    [Fact]
    public void Down_RefillsFromTheDistricts_AfterTheTableAndIndexExist()
    {
        var operations = Down().ToList();
        var refill = Assert.Single(operations.OfType<SqlOperation>());

        Assert.Equal(DropTownStreet.RefillFromDistrictsSql, refill.Sql);
        Assert.Equal(
            "INSERT INTO `TownStreet` (`StreetId`, `TownId`) SELECT DISTINCT ds.`StreetId`, d.`TownId` FROM `DistrictStreet` ds JOIN `districts` d ON d.`Id` = ds.`DistrictId`;",
            refill.Sql);
        Assert.Equal(operations.Count - 1, operations.IndexOf(refill));
    }
}
