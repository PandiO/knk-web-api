using knkwebapi_v2.Dtos;
using knkwebapi_v2.Migrations;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Town logistics P9 (PandiO/knk-workspace#127): one additive migration. Six new tables (policies, rules, consumer
/// settings, reservations, planner runs and their explanation lines); nothing is dropped or altered and no SQL runs. One
/// current version per town, warehouse or template; one open reservation per consumer storage and blueprint; one setting
/// per storage and blueprint; rules go with their version, everything they point at is kept.
/// </summary>
public class AddTownLogisticsTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddTownLogistics().UpOperations;

    [Fact]
    public void Up_CreatesSixTables_AndChangesNothingElse()
    {
        Assert.Equal(new[]
            {
                "consumer_replenishments", "logistics_plan_entries", "logistics_planner_runs", "logistics_policies", "logistics_reservations",
                "logistics_rules",
            },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n).ToArray());
        Assert.Empty(Up().OfType<AddColumnOperation>());
        Assert.Empty(Up().OfType<DropTableOperation>());
        Assert.Empty(Up().OfType<DropColumnOperation>());
        Assert.Empty(Up().OfType<AlterColumnOperation>());
        Assert.Empty(Up().OfType<SqlOperation>());
    }

    [Fact]
    public void OneCurrentVersion_OneOpenReservation_OneSettingPerStorageAndBlueprint_AndRulesGoWithTheirVersion()
    {
        var indexes = Up().OfType<CreateIndexOperation>().ToList();
        Assert.True(Assert.Single(indexes, i => i.Table == "logistics_policies" && i.Columns.SequenceEqual(new[] { "CurrentKey" })).IsUnique);
        Assert.True(Assert.Single(indexes, i => i.Table == "logistics_policies" && i.Columns.SequenceEqual(new[] { "LineageId", "Version" })).IsUnique);
        Assert.True(Assert.Single(indexes, i => i.Table == "logistics_reservations" && i.Columns.SequenceEqual(new[] { "OpenKey" })).IsUnique);
        Assert.True(Assert.Single(indexes, i => i.Table == "consumer_replenishments" && i.Columns.SequenceEqual(new[] { "StorageId", "ItemBlueprintId" })).IsUnique);
        var tables = Up().OfType<CreateTableOperation>().ToDictionary(t => t.Name);
        var rules = tables["logistics_rules"].ForeignKeys.ToDictionary(fk => fk.PrincipalTable, fk => fk.OnDelete);
        Assert.Equal(ReferentialAction.Cascade, rules["logistics_policies"]);
        Assert.All(rules.Where(r => r.Key != "logistics_policies"), r => Assert.Equal(ReferentialAction.Restrict, r.Value));
        Assert.All(tables["logistics_reservations"].ForeignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        Assert.Equal(ReferentialAction.Cascade, tables["logistics_plan_entries"].ForeignKeys.Single().OnDelete);
    }

    [Fact]
    public void TheLogisticsSettings_HaveTheirDefaults_AndAreChecked()
    {
        var d = ProductionSettings.Read(null).Logistics!;
        Assert.Equal((10, 30, 7, 4m, 12m, 14), (d.PlannerIntervalMinutes, d.ReservationMinutes, d.ConsumptionWindowDays, d.DefaultReorderHours, d.DefaultTargetHours, d.RunRetentionDays));
        var stored = new ProductionSettingsDto { Logistics = new ProductionLogisticsSettingsDto { PlannerIntervalMinutes = 15 } };
        Assert.Equal(15, ProductionSettings.Normalize(new ProductionSettingsDto(), stored).Logistics!.PlannerIntervalMinutes); // null keeps the stored section
        var ex = Assert.Throws<ArgumentException>(() => ProductionSettings.Normalize(new ProductionSettingsDto
        {
            Logistics = new ProductionLogisticsSettingsDto { DefaultReorderHours = 20, DefaultTargetHours = 10 },
        }));
        Assert.Contains("production.logistics.defaultReorderHours", ex.Message);
        Assert.Throws<ArgumentException>(() => ProductionSettings.Normalize(new ProductionSettingsDto { Logistics = new ProductionLogisticsSettingsDto { PlannerIntervalMinutes = 0 } }));
    }
}
