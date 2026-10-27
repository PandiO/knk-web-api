using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// PandiO/knk-workspace#167: per-door pass-through styles. Each door offers a set of styles (animated, snap,
    /// walk-path, teleport) plus a default style; the gate structure can override both. Existing doors get the
    /// styles players could pick before #167 (animated, walk-path, teleport; snap off) and the animated default,
    /// so nothing changes until an admin edits a door. The structure overrides start empty (null = no override).
    /// </summary>
    public partial class AddGatePassThroughStyles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowAnimatedPassThrough",
                table: "gate_doors",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowSnapPassThrough",
                table: "gate_doors",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowTeleportPassThrough",
                table: "gate_doors",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowWalkPathPassThrough",
                table: "gate_doors",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultPassThroughMethod",
                table: "gate_doors",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Default")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "AllowAnimatedPassThroughOverride",
                table: "gate_structures",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowSnapPassThroughOverride",
                table: "gate_structures",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowTeleportPassThroughOverride",
                table: "gate_structures",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowWalkPathPassThroughOverride",
                table: "gate_structures",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultPassThroughMethodOverride",
                table: "gate_structures",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AllowAnimatedPassThrough", table: "gate_doors");
            migrationBuilder.DropColumn(name: "AllowSnapPassThrough", table: "gate_doors");
            migrationBuilder.DropColumn(name: "AllowTeleportPassThrough", table: "gate_doors");
            migrationBuilder.DropColumn(name: "AllowWalkPathPassThrough", table: "gate_doors");
            migrationBuilder.DropColumn(name: "DefaultPassThroughMethod", table: "gate_doors");
            migrationBuilder.DropColumn(name: "AllowAnimatedPassThroughOverride", table: "gate_structures");
            migrationBuilder.DropColumn(name: "AllowSnapPassThroughOverride", table: "gate_structures");
            migrationBuilder.DropColumn(name: "AllowTeleportPassThroughOverride", table: "gate_structures");
            migrationBuilder.DropColumn(name: "AllowWalkPathPassThroughOverride", table: "gate_structures");
            migrationBuilder.DropColumn(name: "DefaultPassThroughMethodOverride", table: "gate_structures");
            // Users who picked Snap (value 3) fall back to the animated default.
            migrationBuilder.Sql("UPDATE users SET GatePassThroughMethodDefault = 0 WHERE GatePassThroughMethodDefault = 3;");
        }
    }
}
