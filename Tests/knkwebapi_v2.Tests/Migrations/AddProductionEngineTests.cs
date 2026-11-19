using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Production engine P3 (PandiO/knk-workspace#86): one additive migration. Thirteen new tables, the seeded catalogue
/// (eight kinds × ten levels), one default climate zone and three weather types, and the foreign key from
/// <c>production_structures.ProductionKindId</c> (a plain indexed id since P2) to the kinds, Restrict. Nothing is dropped
/// or altered and no SQL runs.
/// </summary>
public class AddProductionEngineTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddProductionEngine().UpOperations;

    [Fact]
    public void Up_CreatesTheEngineTables()
    {
        Assert.Equal(new[]
        {
            "climate_zone_towns", "climate_zone_weathers", "climate_zones", "production_cycle_records",
            "production_kind_level_items", "production_kind_levels", "production_kinds", "production_line_inputs",
            "production_lines", "production_modifiers", "weather_rolls", "weather_type_effects", "weather_types",
        }, Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Up_SeedsTheCatalogue_TenLevelsPerKind()
    {
        var inserts = Up().OfType<InsertDataOperation>().ToList();
        Assert.Equal(8, inserts.Where(i => i.Table == "production_kinds").Sum(i => i.Values.GetLength(0)));
        Assert.Equal(80, inserts.Where(i => i.Table == "production_kind_levels").Sum(i => i.Values.GetLength(0)));
        Assert.Equal(1, inserts.Where(i => i.Table == "climate_zones").Sum(i => i.Values.GetLength(0)));
        Assert.Equal(3, inserts.Where(i => i.Table == "weather_types").Sum(i => i.Values.GetLength(0)));
    }

    [Fact]
    public void Up_AddsTheKindForeignKey_Restrict_AndChangesNothingElse()
    {
        var fk = Assert.Single(Up().OfType<AddForeignKeyOperation>());
        Assert.Equal(("production_structures", "ProductionKindId", "production_kinds", ReferentialAction.Restrict),
            (fk.Table, fk.Columns.Single(), fk.PrincipalTable, fk.OnDelete));
        Assert.Empty(Up().OfType<DropTableOperation>());
        Assert.Empty(Up().OfType<DropColumnOperation>());
        Assert.Empty(Up().OfType<AlterColumnOperation>());
        Assert.Empty(Up().OfType<AddColumnOperation>());
        Assert.Empty(Up().OfType<SqlOperation>());
    }

    [Fact]
    public void ProductionLines_AreIndexedOnTheirDueTime()
    {
        var index = Assert.Single(Up().OfType<CreateIndexOperation>(), i => i.Table == "production_lines" && i.Columns.SequenceEqual(new[] { "NextSettleAt" }));
        Assert.False(index.IsUnique);
    }
}
