using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-34 link 2: the statistics migration is additive only (charter §3) — ten new tables and their
/// indexes, nothing altered, dropped or seeded. Applied on MySQL 8 by MySql/StatisticsUpsertMySqlTests.
/// </summary>
public class AddPlayerStatisticsTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddPlayerStatistics().UpOperations;

    [Fact]
    public void Up_OnlyCreatesTheTenStatisticsTables()
    {
        Assert.Equal(new[]
        {
            "player_pvp_kill_pairs_daily", "player_stat_batches", "player_stat_daily", "player_stat_profiles", "player_stat_sessions",
            "player_stat_totals", "player_stat_visibility", "player_title_changes", "statistics_projected_sources",
            "statistics_projection_cursors"
        }, Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(Up(), op => Assert.True(op is CreateTableOperation or CreateIndexOperation, op.GetType().Name));
    }

    [Fact]
    public void Up_MakesTheIdempotencyKeysUnique()
    {
        var unique = Up().OfType<CreateIndexOperation>().Where(i => i.IsUnique).Select(i => (i.Table, string.Join(",", i.Columns))).OrderBy(x => x.Table).ToList();

        Assert.Equal(new[] { ("player_stat_sessions", "SessionKey"), ("player_title_changes", "CurrencyEntryId") }, unique);
    }

    [Fact]
    public void Up_AddsNoForeignKeys()
    {
        Assert.All(Up().OfType<CreateTableOperation>(), t => Assert.Empty(t.ForeignKeys));
    }

    [Fact]
    public void Down_DropsOnlyTheTenTables()
    {
        Assert.Equal(10, new AddPlayerStatistics().DownOperations.OfType<DropTableOperation>().Count());
        Assert.All(new AddPlayerStatistics().DownOperations, op => Assert.IsType<DropTableOperation>(op));
    }
}
