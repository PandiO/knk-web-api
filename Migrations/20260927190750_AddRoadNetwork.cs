using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddRoadNetwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "road_profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RoadClass = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CostMultiplier = table.Column<double>(type: "double", nullable: false),
                    MaterialsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WidthMin = table.Column<int>(type: "int", nullable: false),
                    WidthMax = table.Column<int>(type: "int", nullable: false),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ScopeTownIdsJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "road_tiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    World = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TileX = table.Column<int>(type: "int", nullable: false),
                    TileZ = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    BuiltAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    BuilderVersion = table.Column<int>(type: "int", nullable: false),
                    Dirty = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CellCount = table.Column<int>(type: "int", nullable: false),
                    NodeCount = table.Column<int>(type: "int", nullable: false),
                    EdgeCount = table.Column<int>(type: "int", nullable: false),
                    LevelCount = table.Column<int>(type: "int", nullable: false),
                    WarningsJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "road_surveys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    World = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProfileId = table.Column<int>(type: "int", nullable: true),
                    StartedByUserId = table.Column<int>(type: "int", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    BreadcrumbJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_road_surveys_road_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "road_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_road_surveys_users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "road_nodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    World = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    Z = table.Column<int>(type: "int", nullable: false),
                    TileId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ComponentId = table.Column<int>(type: "int", nullable: false),
                    Locked = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_road_nodes_road_tiles_TileId",
                        column: x => x.TileId,
                        principalTable: "road_tiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "road_seeds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    World = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    Z = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SurveyId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_road_seeds_road_surveys_SurveyId",
                        column: x => x.SurveyId,
                        principalTable: "road_surveys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "road_edges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FromNodeId = table.Column<int>(type: "int", nullable: false),
                    ToNodeId = table.Column<int>(type: "int", nullable: false),
                    TileId = table.Column<int>(type: "int", nullable: false),
                    World = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GeometryJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Length = table.Column<double>(type: "double", nullable: false),
                    MinX = table.Column<int>(type: "int", nullable: false),
                    MinY = table.Column<int>(type: "int", nullable: false),
                    MinZ = table.Column<int>(type: "int", nullable: false),
                    MaxX = table.Column<int>(type: "int", nullable: false),
                    MaxY = table.Column<int>(type: "int", nullable: false),
                    MaxZ = table.Column<int>(type: "int", nullable: false),
                    AvgWidth = table.Column<double>(type: "double", nullable: false),
                    ProfileId = table.Column<int>(type: "int", nullable: true),
                    StreetId = table.Column<int>(type: "int", nullable: true),
                    StreetSource = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CostMultiplier = table.Column<double>(type: "double", nullable: false),
                    Flags = table.Column<int>(type: "int", nullable: false),
                    GateDoorIdsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DomainIdsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RegionIdsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_road_edges_road_nodes_FromNodeId",
                        column: x => x.FromNodeId,
                        principalTable: "road_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_road_edges_road_nodes_ToNodeId",
                        column: x => x.ToNodeId,
                        principalTable: "road_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_road_edges_road_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "road_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_road_edges_road_tiles_TileId",
                        column: x => x.TileId,
                        principalTable: "road_tiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_road_edges_streets_StreetId",
                        column: x => x.StreetId,
                        principalTable: "streets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_road_edges_FromNodeId_ToNodeId",
                table: "road_edges",
                columns: new[] { "FromNodeId", "ToNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_road_edges_ProfileId",
                table: "road_edges",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_road_edges_StreetId",
                table: "road_edges",
                column: "StreetId");

            migrationBuilder.CreateIndex(
                name: "IX_road_edges_TileId",
                table: "road_edges",
                column: "TileId");

            migrationBuilder.CreateIndex(
                name: "IX_road_edges_ToNodeId",
                table: "road_edges",
                column: "ToNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_road_edges_World_MinX_MinZ",
                table: "road_edges",
                columns: new[] { "World", "MinX", "MinZ" });

            migrationBuilder.CreateIndex(
                name: "IX_road_nodes_TileId",
                table: "road_nodes",
                column: "TileId");

            migrationBuilder.CreateIndex(
                name: "IX_road_nodes_World_ComponentId",
                table: "road_nodes",
                columns: new[] { "World", "ComponentId" });

            migrationBuilder.CreateIndex(
                name: "IX_road_nodes_World_X_Y_Z",
                table: "road_nodes",
                columns: new[] { "World", "X", "Y", "Z" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_road_profiles_Name",
                table: "road_profiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_road_seeds_SurveyId",
                table: "road_seeds",
                column: "SurveyId");

            migrationBuilder.CreateIndex(
                name: "IX_road_seeds_World",
                table: "road_seeds",
                column: "World");

            migrationBuilder.CreateIndex(
                name: "IX_road_surveys_ProfileId",
                table: "road_surveys",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_road_surveys_StartedByUserId",
                table: "road_surveys",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_road_surveys_World_StartedAt",
                table: "road_surveys",
                columns: new[] { "World", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_road_tiles_World_TileX_TileZ",
                table: "road_tiles",
                columns: new[] { "World", "TileX", "TileZ" },
                unique: true);

            // Bootstrap profile (docs/specs/navigation/DESIGN.md §3.1, plan Phase 1.2) so a build
            // works before the first survey walk. Materials are RoadMaterialDto rows as
            // JsonColumn writes them; COBBLESTONE and STONE_BRICKS are ambiguous (kerbs and floors).
            var seededAt = new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table: "road_profiles",
                columns: new[] { "Name", "RoadClass", "CostMultiplier", "MaterialsJson", "WidthMin", "WidthMax", "SampleCount", "Enabled", "ScopeTownIdsJson", "StatsJson", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    {
                        "Default road", "Road", 1.0,
                        "[" +
                        "{\"material\":\"GRAVEL\",\"role\":\"Surface\",\"ambiguous\":false,\"centreShare\":0,\"edgeShare\":0,\"samples\":0}," +
                        "{\"material\":\"DIRT_PATH\",\"role\":\"Surface\",\"ambiguous\":false,\"centreShare\":0,\"edgeShare\":0,\"samples\":0}," +
                        "{\"material\":\"COARSE_DIRT\",\"role\":\"Surface\",\"ambiguous\":false,\"centreShare\":0,\"edgeShare\":0,\"samples\":0}," +
                        "{\"material\":\"COBBLESTONE\",\"role\":\"Surface\",\"ambiguous\":true,\"centreShare\":0,\"edgeShare\":0,\"samples\":0}," +
                        "{\"material\":\"STONE_BRICKS\",\"role\":\"Surface\",\"ambiguous\":true,\"centreShare\":0,\"edgeShare\":0,\"samples\":0}," +
                        "{\"material\":\"COBBLESTONE_SLAB\",\"role\":\"Accent\",\"ambiguous\":false,\"centreShare\":0,\"edgeShare\":0,\"samples\":0}," +
                        "{\"material\":\"COBBLESTONE_STAIRS\",\"role\":\"Accent\",\"ambiguous\":false,\"centreShare\":0,\"edgeShare\":0,\"samples\":0}," +
                        "{\"material\":\"STONE_BRICK_SLAB\",\"role\":\"Accent\",\"ambiguous\":false,\"centreShare\":0,\"edgeShare\":0,\"samples\":0}," +
                        "{\"material\":\"STONE_BRICK_STAIRS\",\"role\":\"Accent\",\"ambiguous\":false,\"centreShare\":0,\"edgeShare\":0,\"samples\":0}," +
                        "{\"material\":\"MOSSY_COBBLESTONE\",\"role\":\"Accent\",\"ambiguous\":false,\"centreShare\":0,\"edgeShare\":0,\"samples\":0}," +
                        "{\"material\":\"MOSSY_STONE_BRICKS\",\"role\":\"Accent\",\"ambiguous\":false,\"centreShare\":0,\"edgeShare\":0,\"samples\":0}," +
                        "{\"material\":\"CRACKED_STONE_BRICKS\",\"role\":\"Accent\",\"ambiguous\":false,\"centreShare\":0,\"edgeShare\":0,\"samples\":0}" +
                        "]",
                        1, 7, 0, true, null, "{}", seededAt, seededAt
                    }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "road_edges");

            migrationBuilder.DropTable(
                name: "road_seeds");

            migrationBuilder.DropTable(
                name: "road_nodes");

            migrationBuilder.DropTable(
                name: "road_surveys");

            migrationBuilder.DropTable(
                name: "road_tiles");

            migrationBuilder.DropTable(
                name: "road_profiles");
        }
    }
}
