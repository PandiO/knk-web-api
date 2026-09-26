using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-20 Phase 1: the discovery tables, the unique (UserId, DomainId) idempotency index, and the
/// four seeded type rules (developer decisions 2026-09-26). The migration was also applied, rolled
/// back and re-applied on MySQL 8 when written; the Migrations (fresh DB) workflow repeats that.
/// </summary>
public class AddDomainDiscoveryTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddDomainDiscovery().UpOperations;

    [Fact]
    public void Up_CreatesTheThreeTables()
    {
        Assert.Equal(new[] { "discovery_reward_rules", "domain_discovery_overrides", "user_domain_discoveries" },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n));
    }

    [Fact]
    public void Up_MakesUserAndDomainUnique()
    {
        var unique = Up().OfType<CreateIndexOperation>().Single(i => i.IsUnique);

        Assert.Equal("user_domain_discoveries", unique.Table);
        Assert.Equal(new[] { "UserId", "DomainId" }, unique.Columns);
    }

    [Fact]
    public void Up_SeedsTheDecidedRules()
    {
        var insert = Up().OfType<InsertDataOperation>().Single();
        var columns = insert.Columns.ToList();
        var rows = Enumerable.Range(0, insert.Values.GetLength(0))
            .ToDictionary(r => (string)insert.Values[r, columns.IndexOf("DomainType")]!, r => (Func<string, object?>)(c => insert.Values[r, columns.IndexOf(c)]));

        Assert.Equal(new[] { "District", "GateStructure", "Structure", "Town" }, rows.Keys.OrderBy(k => k));
        Assert.All(rows.Values, row => Assert.Equal(true, row("IsEnabled")));

        Assert.Equal((1m, 4m, 2m, 8m, 5, 15, false), Values(rows["Town"]));
        Assert.Equal((0.5m, 2m, 0.5m, 2m, 1, 3, true), Values(rows["District"]));
        Assert.Equal((0.05m, 0.25m, 0.05m, 0.25m, 0, 0, true), Values(rows["Structure"]));
        Assert.Equal((0.05m, 0.25m, 0.05m, 0.25m, 0, 0, true), Values(rows["GateStructure"]));
    }

    private static (decimal, decimal, decimal, decimal, int, int, bool) Values(Func<string, object?> row) => (
        (decimal)row("ExpUnitsMin")!, (decimal)row("ExpUnitsMax")!, (decimal)row("CoinSalaryHoursMin")!, (decimal)row("CoinSalaryHoursMax")!,
        (int)row("GemsMin")!, (int)row("GemsMax")!, (bool)row("IncludeAncestors")!);

    [Fact]
    public void Down_DropsTheThreeTables()
    {
        Assert.Equal(3, new AddDomainDiscovery().DownOperations.OfType<DropTableOperation>().Count());
    }
}
