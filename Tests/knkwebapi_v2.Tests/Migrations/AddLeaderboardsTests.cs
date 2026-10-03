using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-34 link 5: the leaderboard migration is additive only (charter §3) — two new tables and
/// their indexes; the only FK is entries → snapshots (cascade), never to users.
/// </summary>
public class AddLeaderboardsTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddLeaderboards().UpOperations;

    [Fact]
    public void Up_OnlyCreatesTheTwoLeaderboardTables()
    {
        Assert.Equal(new[] { "leaderboard_snapshot_entries", "leaderboard_snapshots" },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(Up(), op => Assert.True(op is CreateTableOperation or CreateIndexOperation, op.GetType().Name));
    }

    [Fact]
    public void Up_EntriesBelongToTheirSnapshot_AndNothingReferencesUsers()
    {
        var foreignKeys = Up().OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys).ToList();

        var fk = Assert.Single(foreignKeys);
        Assert.Equal(("leaderboard_snapshot_entries", "leaderboard_snapshots", ReferentialAction.Cascade), (fk.Table, fk.PrincipalTable, fk.OnDelete));
    }

    [Fact]
    public void Down_DropsOnlyTheTwoTables()
    {
        Assert.All(new AddLeaderboards().DownOperations, op => Assert.IsType<DropTableOperation>(op));
        Assert.Equal(2, new AddLeaderboards().DownOperations.Count);
    }
}
