using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// KNG-59: one permission grant row per (holder, node). Deletes existing duplicates, keeping
    /// the same row PermissionGrantService.UpsertByNodeAsync keeps (the lowest-id active row, else
    /// the lowest-id row), then makes the (HolderId, Node) index unique. The duplicates are not
    /// audited: they predate the rule and the kept row still decides the node.
    /// </summary>
    public partial class UniquePermissionGrantHolderNode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Grouping uses the column's collation (utf8mb4_general_ci), the same comparison the
            // unique index applies, so every pair the index would reject is collapsed here.
            migrationBuilder.Sql(@"
DELETE g FROM permission_grants g
JOIN (
    SELECT HolderId, Node,
           COALESCE(MIN(CASE WHEN ExpiresAt IS NULL OR ExpiresAt > UTC_TIMESTAMP(6) THEN Id END), MIN(Id)) AS KeepId
    FROM permission_grants
    GROUP BY HolderId, Node
    HAVING COUNT(*) > 1
) k ON g.HolderId = k.HolderId AND g.Node = k.Node AND g.Id <> k.KeepId;");

            // Add the unique index before dropping the old one: MySQL refuses to drop the only
            // index backing the HolderId foreign key.
            migrationBuilder.CreateIndex(
                name: "UX_permission_grants_HolderId_Node",
                table: "permission_grants",
                columns: new[] { "HolderId", "Node" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_permission_grants_HolderId_Node",
                table: "permission_grants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_permission_grants_HolderId_Node",
                table: "permission_grants",
                columns: new[] { "HolderId", "Node" });

            migrationBuilder.DropIndex(
                name: "UX_permission_grants_HolderId_Node",
                table: "permission_grants");
        }
    }
}
