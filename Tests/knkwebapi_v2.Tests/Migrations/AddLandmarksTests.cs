using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Off-road navigation phase 5.1 (PandiO/knk-workspace#708, charter § 3.3): the only migration of the chain is additive,
/// one new <c>landmarks</c> table and its indexes, no change to an existing table. The pin's Location can't be deleted
/// under a landmark (RESTRICT); deleting the domain only clears the link (SET NULL).
/// </summary>
public class AddLandmarksTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddLandmarks().UpOperations;

    [Fact]
    public void Up_OnlyCreatesTheLandmarkTableAndItsIndexes()
    {
        var table = Assert.Single(Up().OfType<CreateTableOperation>());
        Assert.Equal("landmarks", table.Name);
        Assert.All(Up(), op => Assert.True(op is CreateTableOperation or CreateIndexOperation, op.GetType().Name));
        Assert.All(Up().OfType<CreateIndexOperation>(), i => Assert.Equal("landmarks", i.Table));
        Assert.Equal(
            new[] { "CreatedAt", "DomainId", "ExtentKind", "Id", "Kind", "LocationId", "Name", "NavigationEnabled", "Preference", "Radius", "RegionId" },
            table.Columns.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Up_TheFieldsOfDesign63()
    {
        var columns = Assert.Single(Up().OfType<CreateTableOperation>()).Columns.ToDictionary(c => c.Name);
        Assert.Equal(100, columns["Name"].MaxLength);
        Assert.False(columns["Name"].IsNullable);
        Assert.False(columns["LocationId"].IsNullable);
        Assert.True(columns["DomainId"].IsNullable);
        Assert.Equal(128, columns["RegionId"].MaxLength);
        Assert.True(columns["Radius"].IsNullable);
        Assert.Equal(typeof(string), columns["ExtentKind"].ClrType);
        Assert.Equal(typeof(string), columns["Kind"].ClrType);
    }

    [Fact]
    public void Up_PreferenceIsChecked()
    {
        var check = Assert.Single(Assert.Single(Up().OfType<CreateTableOperation>()).CheckConstraints);
        Assert.Equal("`Preference` >= 0.25 AND `Preference` <= 4", check.Sql);
    }

    [Fact]
    public void Up_LocationRestricts_DomainIsClearedOnDelete()
    {
        var fks = Assert.Single(Up().OfType<CreateTableOperation>()).ForeignKeys.ToDictionary(f => f.PrincipalTable);
        Assert.Equal(2, fks.Count);
        Assert.Equal(ReferentialAction.Restrict, fks["locations"].OnDelete);
        Assert.Equal(ReferentialAction.SetNull, fks["domains"].OnDelete);
    }

    [Fact]
    public void Down_DropsOnlyTheTable()
    {
        var drop = Assert.IsType<DropTableOperation>(Assert.Single(new AddLandmarks().DownOperations));
        Assert.Equal("landmarks", drop.Name);
    }
}
