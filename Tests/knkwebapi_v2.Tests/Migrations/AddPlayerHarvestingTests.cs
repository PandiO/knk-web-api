using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Player harvesting P8a (PandiO/knk-workspace#685): one additive migration. Four new tables (rules, pools, the harvest
/// history, the carried fractions) and two columns with defaults (the kind's pool floor 0.5, the cycle record's pool
/// factor 1); nothing is dropped or altered and no SQL runs. A key books once, one pool per area, a rule with history
/// can't be deleted, and the history never takes a user with it.
/// </summary>
public class AddPlayerHarvestingTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddPlayerHarvesting().UpOperations;

    [Fact]
    public void Up_CreatesFourTables_AndTwoColumnsWithDefaults_AndChangesNothingElse()
    {
        Assert.Equal(new[] { "harvest_accruals", "harvest_rules", "player_harvests", "resource_pools" },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n).ToArray());
        var columns = Up().OfType<AddColumnOperation>().ToDictionary(c => c.Table + "." + c.Name);
        Assert.Equal(2, columns.Count);
        Assert.Equal(0.5m, columns["production_kinds.PoolFloor"].DefaultValue);
        Assert.Equal(1m, columns["production_cycle_records.PoolFactor"].DefaultValue);
        Assert.Empty(Up().OfType<DropTableOperation>());
        Assert.Empty(Up().OfType<DropColumnOperation>());
        Assert.Empty(Up().OfType<AlterColumnOperation>());
        Assert.Empty(Up().OfType<SqlOperation>());
    }

    [Fact]
    public void AKeyBooksOnce_OnePoolPerArea_AndTheHistoryKeepsItsRuleAndUser()
    {
        var indexes = Up().OfType<CreateIndexOperation>().ToList();
        Assert.True(Assert.Single(indexes, i => i.Table == "player_harvests" && i.Columns.SequenceEqual(new[] { "IdempotencyKey" })).IsUnique);
        Assert.True(Assert.Single(indexes, i => i.Table == "resource_pools" && i.Columns.SequenceEqual(new[] { "ProductionAreaId" })).IsUnique);
        Assert.True(Assert.Single(indexes, i => i.Table == "harvest_accruals" && i.Columns.SequenceEqual(new[] { "UserId", "HarvestRuleId" })).IsUnique);
        var tables = Up().OfType<CreateTableOperation>().ToDictionary(t => t.Name);
        Assert.All(tables["player_harvests"].ForeignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        Assert.Equal(ReferentialAction.Cascade, tables["resource_pools"].ForeignKeys.Single().OnDelete);
        var rules = tables["harvest_rules"].ForeignKeys.ToDictionary(fk => fk.PrincipalTable, fk => fk.OnDelete);
        Assert.Equal(ReferentialAction.Cascade, rules["production_kinds"]);
        Assert.Equal(ReferentialAction.Restrict, rules["item_blueprints"]);
        Assert.Equal(ReferentialAction.SetNull, rules["title_brackets"]);
    }
}
