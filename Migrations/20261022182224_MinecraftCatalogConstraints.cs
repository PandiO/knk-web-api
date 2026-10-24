using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// KNG-200 phase 5 (docs/specs/minecraft-catalog/IMPLEMENTATION_PLAN.md §8 step 5): a block state's
    /// StateKey becomes unique and its MaterialRefId required. Apply it after the API has started once on
    /// the phase 2 schema (the backfill links and keys every block ref) and GET api/MinecraftCatalog/audit
    /// reports readyForConstraints. A table that isn't clean stops this migration before any change;
    /// nothing is deleted.
    /// </summary>
    public partial class MinecraftCatalogConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refuse, with the reason, while block refs are unlinked, unkeyed or duplicated. MySQL only raises
            // errors from stored programs, so the check is a short-lived procedure.
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS kng200_check_block_refs;");
            migrationBuilder.Sql(@"
CREATE PROCEDURE kng200_check_block_refs()
BEGIN
    IF EXISTS (SELECT 1 FROM minecraftblockrefs WHERE MaterialRefId IS NULL OR StateKey IS NULL)
       OR EXISTS (SELECT 1 FROM minecraftblockrefs GROUP BY StateKey HAVING COUNT(*) > 1) THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'KNG-200: block refs not clean. Start the API once, fix GET api/MinecraftCatalog/audit, then retry.';
    END IF;
END;");
            migrationBuilder.Sql("CALL kng200_check_block_refs();");
            migrationBuilder.Sql("DROP PROCEDURE kng200_check_block_refs;");

            migrationBuilder.DropIndex(
                name: "IX_minecraftblockrefs_StateKey",
                table: "minecraftblockrefs");

            migrationBuilder.AlterColumn<int>(
                name: "MaterialRefId",
                table: "minecraftblockrefs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_minecraftblockrefs_StateKey",
                table: "minecraftblockrefs",
                column: "StateKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_minecraftblockrefs_StateKey",
                table: "minecraftblockrefs");

            migrationBuilder.AlterColumn<int>(
                name: "MaterialRefId",
                table: "minecraftblockrefs",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateIndex(
                name: "IX_minecraftblockrefs_StateKey",
                table: "minecraftblockrefs",
                column: "StateKey");
        }
    }
}
