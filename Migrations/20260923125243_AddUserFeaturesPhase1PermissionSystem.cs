using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFeaturesPhase1PermissionSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MySQL/InnoDB refuses to MODIFY a column (here: dropping users.Id's AUTO_INCREMENT)
            // while another table's foreign key references it - confirmed against a real local
            // MySQL 8.0 copy ("Cannot change column 'Id': used in a foreign key constraint
            // 'FK_world_tasks_users_AssignedUserId'..."), not something the auto-generated
            // migration accounted for. Drop every inbound FK first, alter the column, then
            // restore each FK with its original delete behavior.
            migrationBuilder.DropForeignKey(
                name: "FK_FormSubmissionProgresses_users_UserId",
                table: "FormSubmissionProgresses");

            migrationBuilder.DropForeignKey(
                name: "FK_linkcodes_users_UserId",
                table: "linkcodes");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_sessions_users_UserId",
                table: "workflow_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_world_tasks_users_AssignedUserId",
                table: "world_tasks");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "users",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .OldAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.CreateTable(
                name: "permission_holders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ChatPrefix = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChatSuffix = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            // Data migration, not auto-generated: every existing `users` row needs a matching
            // `permission_holders` row *before* the FK_users_permission_holders_Id constraint
            // below can be added, since that FK requires users.Id to already resolve against
            // permission_holders.Id. Explicit-value inserts into an AUTO_INCREMENT column are
            // fine in MySQL/InnoDB — the auto-increment counter is bumped to MAX(Id)+1
            // afterwards, so subsequent PermissionGroup/new-User inserts (which also draw their
            // Id from this table) don't collide with the backfilled Ids. Verified against a real
            // local MySQL 8.0 copy with pre-existing `users` rows before this migration shipped.
            migrationBuilder.Sql(
                "INSERT INTO permission_holders (Id, ChatPrefix, ChatSuffix) " +
                "SELECT Id, NULL, NULL FROM users;");

            migrationBuilder.CreateTable(
                name: "permission_grants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    HolderId = table.Column<int>(type: "int", nullable: false),
                    Node = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_permission_grants_permission_holders_HolderId",
                        column: x => x.HolderId,
                        principalTable: "permission_holders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "permission_groups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Weight = table.Column<int>(type: "int", nullable: false),
                    ParentGroupId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_permission_groups_permission_groups_ParentGroupId",
                        column: x => x.ParentGroupId,
                        principalTable: "permission_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_permission_groups_permission_holders_Id",
                        column: x => x.Id,
                        principalTable: "permission_holders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "user_permission_groups",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PermissionGroupId = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_permission_groups", x => new { x.UserId, x.PermissionGroupId });
                    table.ForeignKey(
                        name: "FK_user_permission_groups_permission_groups_PermissionGroupId",
                        column: x => x.PermissionGroupId,
                        principalTable: "permission_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_permission_groups_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_permission_grants_HolderId_Node",
                table: "permission_grants",
                columns: new[] { "HolderId", "Node" });

            migrationBuilder.CreateIndex(
                name: "IX_permission_groups_Name",
                table: "permission_groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_permission_groups_ParentGroupId",
                table: "permission_groups",
                column: "ParentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_user_permission_groups_PermissionGroupId",
                table: "user_permission_groups",
                column: "PermissionGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_users_permission_holders_Id",
                table: "users",
                column: "Id",
                principalTable: "permission_holders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Restore the FKs dropped above, with their original delete behavior
            // (confirmed against the real pre-migration schema via information_schema).
            migrationBuilder.AddForeignKey(
                name: "FK_FormSubmissionProgresses_users_UserId",
                table: "FormSubmissionProgresses",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_linkcodes_users_UserId",
                table: "linkcodes",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_sessions_users_UserId",
                table: "workflow_sessions",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_world_tasks_users_AssignedUserId",
                table: "world_tasks",
                column: "AssignedUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FormSubmissionProgresses_users_UserId",
                table: "FormSubmissionProgresses");

            migrationBuilder.DropForeignKey(
                name: "FK_linkcodes_users_UserId",
                table: "linkcodes");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_sessions_users_UserId",
                table: "workflow_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_world_tasks_users_AssignedUserId",
                table: "world_tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_users_permission_holders_Id",
                table: "users");

            migrationBuilder.DropTable(
                name: "permission_grants");

            migrationBuilder.DropTable(
                name: "user_permission_groups");

            migrationBuilder.DropTable(
                name: "permission_groups");

            migrationBuilder.DropTable(
                name: "permission_holders");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "users",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            // Restore the FKs dropped above, back to their pre-migration shape.
            migrationBuilder.AddForeignKey(
                name: "FK_FormSubmissionProgresses_users_UserId",
                table: "FormSubmissionProgresses",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_linkcodes_users_UserId",
                table: "linkcodes",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_sessions_users_UserId",
                table: "workflow_sessions",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_world_tasks_users_AssignedUserId",
                table: "world_tasks",
                column: "AssignedUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
