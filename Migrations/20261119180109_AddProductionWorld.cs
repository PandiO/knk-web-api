using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionWorld : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_structures_districts_DistrictId",
                table: "structures");

            migrationBuilder.DropForeignKey(
                name: "FK_structures_streets_StreetId",
                table: "structures");

            migrationBuilder.AlterColumn<int>(
                name: "StreetId",
                table: "structures",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "DistrictId",
                table: "structures",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "TownId",
                table: "structures",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    AcceptsDeliveries = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    DefaultStorageId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_warehouses_storages_DefaultStorageId",
                        column: x => x.DefaultStorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_warehouses_structures_Id",
                        column: x => x.Id,
                        principalTable: "structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "production_structures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ProductionKindId = table.Column<int>(type: "int", nullable: true),
                    Level = table.Column<int>(type: "int", nullable: false),
                    ProductionEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AutomaticTransport = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TransportThreshold = table.Column<int>(type: "int", nullable: true),
                    TransportCapacity = table.Column<int>(type: "int", nullable: true),
                    WarehousePreference = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreferredWarehouseId = table.Column<int>(type: "int", nullable: true),
                    RoadConnection = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RoadConnectionMeasuredAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    WorkersTotal = table.Column<int>(type: "int", nullable: false),
                    WorkersPresent = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_structures_structures_Id",
                        column: x => x.Id,
                        principalTable: "structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_production_structures_warehouses_PreferredWarehouseId",
                        column: x => x.PreferredWarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_structures_TownId",
                table: "structures",
                column: "TownId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_structures_DistrictXorTown",
                table: "structures",
                sql: "(`DistrictId` IS NULL) <> (`TownId` IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_production_structures_PreferredWarehouseId",
                table: "production_structures",
                column: "PreferredWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_production_structures_ProductionKindId",
                table: "production_structures",
                column: "ProductionKindId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_DefaultStorageId",
                table: "warehouses",
                column: "DefaultStorageId");

            migrationBuilder.AddForeignKey(
                name: "FK_structures_districts_DistrictId",
                table: "structures",
                column: "DistrictId",
                principalTable: "districts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_structures_streets_StreetId",
                table: "structures",
                column: "StreetId",
                principalTable: "streets",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_structures_towns_TownId",
                table: "structures",
                column: "TownId",
                principalTable: "towns",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_structures_districts_DistrictId",
                table: "structures");

            migrationBuilder.DropForeignKey(
                name: "FK_structures_streets_StreetId",
                table: "structures");

            migrationBuilder.DropForeignKey(
                name: "FK_structures_towns_TownId",
                table: "structures");

            migrationBuilder.DropTable(
                name: "production_structures");

            migrationBuilder.DropTable(
                name: "warehouses");

            migrationBuilder.DropIndex(
                name: "IX_structures_TownId",
                table: "structures");

            migrationBuilder.DropCheckConstraint(
                name: "CK_structures_DistrictXorTown",
                table: "structures");

            migrationBuilder.DropColumn(
                name: "TownId",
                table: "structures");

            migrationBuilder.AlterColumn<int>(
                name: "StreetId",
                table: "structures",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DistrictId",
                table: "structures",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_structures_districts_DistrictId",
                table: "structures",
                column: "DistrictId",
                principalTable: "districts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_structures_streets_StreetId",
                table: "structures",
                column: "StreetId",
                principalTable: "streets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
