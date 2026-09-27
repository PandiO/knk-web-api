using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Json;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-27 Phase 1: the six road tables, the unique indexes that carry the design's invariants
/// (docs/specs/navigation/DESIGN.md §3) and the bootstrap "Default road" profile. The migration
/// was also applied, rolled back and re-applied on MySQL 8 when written; the Migrations (fresh DB)
/// workflow repeats that.
/// </summary>
public class AddRoadNetworkTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddRoadNetwork().UpOperations;

    [Fact]
    public void Up_CreatesTheSixTables()
    {
        Assert.Equal(new[] { "road_edges", "road_nodes", "road_profiles", "road_seeds", "road_surveys", "road_tiles" },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n));
    }

    [Fact]
    public void Up_UniqueIndexesCarryTheDesignInvariants()
    {
        var unique = Up().OfType<CreateIndexOperation>().Where(i => i.IsUnique)
            .ToDictionary(i => i.Table, i => i.Columns);

        Assert.Equal(new[] { "Name" }, unique["road_profiles"]);
        Assert.Equal(new[] { "World", "TileX", "TileZ" }, unique["road_tiles"]);
        Assert.Equal(new[] { "World", "X", "Y", "Z" }, unique["road_nodes"]);
        Assert.Equal(new[] { "FromNodeId", "ToNodeId" }, unique["road_edges"]);
        Assert.Equal(4, unique.Count);
    }

    [Fact]
    public void Up_IndexedStringsHaveAMaxLength()
    {
        // Pomelo maps an unbounded string to longtext, which MySQL can't index (plan 1.2).
        var tables = Up().OfType<CreateTableOperation>().ToDictionary(t => t.Name);
        foreach (var index in Up().OfType<CreateIndexOperation>())
        {
            foreach (var column in index.Columns)
            {
                var definition = tables[index.Table].Columns.Single(c => c.Name == column);
                if (definition.ClrType == typeof(string))
                {
                    Assert.True(definition.MaxLength is > 0, $"{index.Table}.{column} is an indexed string without a max length");
                }
            }
        }
    }

    [Fact]
    public void Up_EdgesCascadeFromBothNodesAndSetNullFromStreetAndProfile()
    {
        var edges = Up().OfType<CreateTableOperation>().Single(t => t.Name == "road_edges");
        var byColumn = edges.ForeignKeys.ToDictionary(fk => fk.Columns.Single(), fk => (fk.PrincipalTable, fk.OnDelete));

        Assert.Equal(("road_nodes", ReferentialAction.Cascade), byColumn["FromNodeId"]);
        Assert.Equal(("road_nodes", ReferentialAction.Cascade), byColumn["ToNodeId"]);
        Assert.Equal(("road_tiles", ReferentialAction.Cascade), byColumn["TileId"]);
        Assert.Equal(("streets", ReferentialAction.SetNull), byColumn["StreetId"]);
        Assert.Equal(("road_profiles", ReferentialAction.SetNull), byColumn["ProfileId"]);
    }

    [Fact]
    public void Up_SurveyKeepsItsUserRowRestrict()
    {
        var surveys = Up().OfType<CreateTableOperation>().Single(t => t.Name == "road_surveys");
        var user = surveys.ForeignKeys.Single(fk => fk.Columns.Single() == "StartedByUserId");

        Assert.Equal("users", user.PrincipalTable);
        Assert.Equal(ReferentialAction.Restrict, user.OnDelete);
    }

    [Fact]
    public void Up_SeedsTheDefaultRoadProfile()
    {
        var insert = Up().OfType<InsertDataOperation>().Single();
        var columns = insert.Columns.ToList();
        Assert.Equal("road_profiles", insert.Table);
        Assert.Equal(1, insert.Values.GetLength(0));
        object? Value(string column) => insert.Values[0, columns.IndexOf(column)];

        Assert.Equal("Default road", Value("Name"));
        Assert.Equal(nameof(RoadClass.Road), Value("RoadClass"));
        Assert.Equal((1, 7, true, "{}"), (Value("WidthMin"), Value("WidthMax"), Value("Enabled"), Value("StatsJson")));
        Assert.Null(Value("ScopeTownIdsJson"));

        // The column must read back through the same DTO the API and the plugin use.
        var materials = JsonColumn.DeserializeList<RoadMaterialDto>((string)Value("MaterialsJson")!);
        Assert.Equal(
            new[] { "GRAVEL", "DIRT_PATH", "COARSE_DIRT", "COBBLESTONE", "STONE_BRICKS" },
            materials.Where(m => m.Role == RoadMaterialRole.Surface).Select(m => m.Material));
        Assert.Equal(
            new[] { "COBBLESTONE_SLAB", "COBBLESTONE_STAIRS", "STONE_BRICK_SLAB", "STONE_BRICK_STAIRS", "MOSSY_COBBLESTONE", "MOSSY_STONE_BRICKS", "CRACKED_STONE_BRICKS" },
            materials.Where(m => m.Role == RoadMaterialRole.Accent).Select(m => m.Material));
        Assert.Equal(new[] { "COBBLESTONE", "STONE_BRICKS" }, materials.Where(m => m.Ambiguous).Select(m => m.Material));

        // ...and writing it back yields the same document (no key or enum drift between seed and code).
        Assert.Equal(
            JsonSerializer.Serialize(JsonDocument.Parse((string)Value("MaterialsJson")!)),
            JsonSerializer.Serialize(JsonDocument.Parse(JsonColumn.Serialize(materials))));
    }

    [Fact]
    public void Down_DropsTheSixTables()
    {
        Assert.Equal(6, new AddRoadNetwork().DownOperations.OfType<DropTableOperation>().Count());
    }
}
