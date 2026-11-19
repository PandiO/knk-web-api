using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Territory V1 (PandiO/knk-workspace#326): one additive migration. New tables for kingdoms, provinces, crown land and
/// conflicts; new columns default to "no change" on existing rows (power 0, not a capital, no kingdom or province, no
/// loyalty, flag None, production settings null = flag off and the default ranges). No row is written.
/// </summary>
public class AddTerritoryTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddTerritory().UpOperations;

    private static AddColumnOperation Column(string table, string name) =>
        Up().OfType<AddColumnOperation>().Single(c => c.Table == table && c.Name == name);

    [Fact]
    public void Up_CreatesTheFourTables()
    {
        Assert.Equal(new[] { "crown_lands", "kingdoms", "provinces", "territory_conflicts" },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n));
    }

    [Fact]
    public void Up_NewColumns_LeaveExistingRowsAsTheyWere()
    {
        Assert.Equal(0, Column("towns", "Power").DefaultValue);
        Assert.True(Column("towns", "KingdomId").IsNullable);
        Assert.True(Column("towns", "ProvinceId").IsNullable);
        Assert.Equal(false, Column("districts", "IsCapital").DefaultValue);
        Assert.Equal("None", Column("structures", "TerritoryFlag").DefaultValue);
        Assert.True(Column("structures", "LoyaltyTownId").IsNullable);
        Assert.True(Column("structures", "TerritoryConflictId").IsNullable);
        Assert.True(Column("game_settings", "ProductionSettingsJson").IsNullable);
    }

    [Fact]
    public void Up_DeletingATownOrConflict_OnlyClearsTheStructureLinks_AndHoldersRestrict()
    {
        var fks = Up().OfType<AddForeignKeyOperation>().ToDictionary(f => (f.Table, f.Columns[0]));
        Assert.Equal(ReferentialAction.SetNull, fks[("structures", "LoyaltyTownId")].OnDelete);
        Assert.Equal(ReferentialAction.SetNull, fks[("structures", "TerritoryConflictId")].OnDelete);
        Assert.Equal(ReferentialAction.Restrict, fks[("towns", "KingdomId")].OnDelete);
        Assert.Equal(ReferentialAction.Restrict, fks[("towns", "ProvinceId")].OnDelete);
    }

    [Fact]
    public void Up_KingdomNames_AreUnique_AndProvinceNamesPerKingdom()
    {
        var indexes = Up().OfType<CreateIndexOperation>().Where(i => i.IsUnique).ToDictionary(i => i.Table, i => i.Columns);
        Assert.Equal(new[] { "Name" }, indexes["kingdoms"]);
        Assert.Equal(new[] { "KingdomId", "Name" }, indexes["provinces"]);
    }

    [Fact]
    public void Up_IsAdditiveOnly_NoDataWritten()
    {
        Assert.DoesNotContain(Up(), o => o is DropColumnOperation or DropTableOperation or AlterColumnOperation
            or InsertDataOperation or UpdateDataOperation or DeleteDataOperation or SqlOperation);
    }
}
