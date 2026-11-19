using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Yield areas and surveys P4 (PandiO/knk-workspace#138): one additive migration. Six new tables, the structure's yield
/// source (existing structures stay Configured, their P3 lines keep working), the kind's survey profile (seeded per kind,
/// version 1) and primary multiplier (1.5). Nothing is dropped or altered and no SQL runs.
/// </summary>
public class AddProductionYieldAreasTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddProductionYieldAreas().UpOperations;

    [Fact]
    public void Up_CreatesTheYieldTables()
    {
        Assert.Equal(new[]
        {
            "area_survey_units", "area_surveys", "production_areas", "production_kind_primary_outputs",
            "production_yield_overrides", "production_yield_rows",
        }, Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Up_AddsColumnsWithDefaults_ExistingStructuresStayConfigured()
    {
        var columns = Up().OfType<AddColumnOperation>().ToDictionary(c => $"{c.Table}.{c.Name}");
        Assert.Equal("Configured", columns["production_structures.YieldSource"].DefaultValue);
        Assert.Equal(1.5m, columns["production_kinds.PrimaryMultiplier"].DefaultValue);
        Assert.Equal(1, columns["production_kinds.SurveyProfileVersion"].DefaultValue);
        Assert.True(columns["production_kinds.SurveyProfileJson"].IsNullable);
        Assert.Equal(4, columns.Count);
    }

    [Fact]
    public void Up_SeedsASurveyProfilePerKind_AndChangesNothingElse()
    {
        var updates = Up().OfType<UpdateDataOperation>().Where(u => u.Table == "production_kinds").ToList();
        Assert.Equal(8, updates.Count);
        Assert.Empty(Up().OfType<DropTableOperation>());
        Assert.Empty(Up().OfType<DropColumnOperation>());
        Assert.Empty(Up().OfType<AlterColumnOperation>());
        Assert.Empty(Up().OfType<SqlOperation>());
    }

    [Fact]
    public void Areas_AndOverrides_AreUniquePerStructure_AndSurveysIndexedByAreaAndTime()
    {
        Assert.True(Assert.Single(Up().OfType<CreateIndexOperation>(), i => i.Table == "production_areas"
            && i.Columns.SequenceEqual(new[] { "ProductionStructureId", "Name" })).IsUnique);
        Assert.True(Assert.Single(Up().OfType<CreateIndexOperation>(), i => i.Table == "production_yield_overrides"
            && i.Columns.SequenceEqual(new[] { "ProductionStructureId", "ItemBlueprintId" })).IsUnique);
        Assert.Single(Up().OfType<CreateIndexOperation>(), i => i.Table == "area_surveys"
            && i.Columns.SequenceEqual(new[] { "ProductionAreaId", "SurveyedAt" }));
    }
}
