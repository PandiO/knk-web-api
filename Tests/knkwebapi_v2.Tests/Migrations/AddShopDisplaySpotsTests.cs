using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Display stalls P12 (PandiO/knk-workspace#250): one additive migration. One new table; nothing is dropped, altered or
/// added to existing tables and no SQL runs. A block is one spot, spots go with their shop, and a spot's pin is cleared
/// when its catalogue entry goes.
/// </summary>
public class AddShopDisplaySpotsTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddShopDisplaySpots().UpOperations;

    [Fact]
    public void Up_CreatesTheSpotTable_AndChangesNothingElse()
    {
        Assert.Equal("shop_display_spots", Assert.Single(Up().OfType<CreateTableOperation>()).Name);
        Assert.Empty(Up().OfType<AddColumnOperation>());
        Assert.Empty(Up().OfType<DropTableOperation>());
        Assert.Empty(Up().OfType<DropColumnOperation>());
        Assert.Empty(Up().OfType<AlterColumnOperation>());
        Assert.Empty(Up().OfType<SqlOperation>());
    }

    [Fact]
    public void ABlockIsOneSpot_SpotsGoWithTheirShop_AndAPinIsClearedWithItsEntry()
    {
        var unique = Assert.Single(Up().OfType<CreateIndexOperation>(), i => i.Columns.SequenceEqual(new[] { "WorldName", "X", "Y", "Z" }));
        Assert.True(unique.IsUnique);
        var fks = Assert.Single(Up().OfType<CreateTableOperation>()).ForeignKeys;
        Assert.Equal(ReferentialAction.Cascade, Assert.Single(fks, f => f.PrincipalTable == "shops").OnDelete);
        Assert.Equal(ReferentialAction.SetNull, Assert.Single(fks, f => f.PrincipalTable == "shop_catalogue_entries").OnDelete);
    }
}
