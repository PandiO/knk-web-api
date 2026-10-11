using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-27 rev. 6 Part B (docs/specs/navigation/IMPLEMENTATION_PLAN.md §5.7): tile state, confirmed
/// edges, the proposal table and the D2 data update. Applied, rolled back and re-applied on MySQL 8
/// when written, with seeded tiles: the update curated exactly the built tiles holding a tombstone, a
/// locked node, a designed plaza or a Recorded edge.
/// </summary>
public class AddRoadCuratedTilesTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddRoadCuratedTiles().UpOperations;

    [Fact]
    public void Up_AddsStateCuratedAtAndConfirmed_WithSafeDefaults()
    {
        var columns = Up().OfType<AddColumnOperation>().ToDictionary(c => $"{c.Table}.{c.Name}");

        Assert.Equal("Detected", columns["road_tiles.State"].DefaultValue);
        Assert.False(columns["road_tiles.State"].IsNullable);
        Assert.True(columns["road_tiles.CuratedAt"].IsNullable);
        Assert.Equal(false, columns["road_edges.Confirmed"].DefaultValue);
    }

    [Fact]
    public void Up_CreatesOneProposalRowPerTile_DeletedWithTheTile()
    {
        var table = Up().OfType<CreateTableOperation>().Single(t => t.Name == "road_tile_proposals");
        var tile = table.ForeignKeys.Single();
        var index = Up().OfType<CreateIndexOperation>().Single(i => i.Table == "road_tile_proposals");

        Assert.Equal(("road_tiles", ReferentialAction.Cascade), (tile.PrincipalTable, tile.OnDelete));
        Assert.True(index.IsUnique);
        Assert.Equal(new[] { "TileId" }, index.Columns);
    }

    [Fact]
    public void Up_CuratesOnlyBuiltTilesThatHoldAdminData()
    {
        var sql = Assert.Single(Up().OfType<SqlOperation>()).Sql;

        Assert.Same(AddRoadCuratedTiles.CurateTilesWithAdminDataSql, sql);
        foreach (var part in new[] { "State = 'Curated'", "BuiltAt IS NOT NULL", "'Pruned', 'PrunedEdge'", "Source = 'Manual'",
                     "Locked = 1", "PlazaRadius IS NOT NULL", "Source = 'Recorded'" })
        {
            Assert.Contains(part, sql);
        }
    }
}
