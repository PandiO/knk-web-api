using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// KNG-212 (docs/specs/invitations/IMPLEMENTATION_PLAN.md § 5.1): the invitations and
    /// invitation_redemptions tables and four nullable users columns. Additive only. Grandfathering
    /// (decision I3): every active, non-deleted account that exists now is admitted as of its creation,
    /// so the plugin's admission gate (KNG-214) never stops an existing player. EmailVerifiedAt starts
    /// NULL for everyone (no address has been proven yet; the old EmailVerified bool was never set).
    /// </summary>
    public partial class AddInvitations : Migration
    {
        public const string GrandfatherSql =
            "UPDATE users SET AdmittedAt = CreatedAt WHERE IsActive = 1 AND DeletedAt IS NULL AND AdmittedAt IS NULL;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AdmittedAt",
                table: "users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvitedByUserId",
                table: "users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvitedViaInvitationId",
                table: "users",
                type: "int",
                nullable: true);

            // Grandfathering: after the column exists, before anything can create an unadmitted account.
            migrationBuilder.Sql(GrandfatherSql);

            migrationBuilder.CreateTable(
                name: "invitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CodeHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CodeHint = table.Column<string>(type: "char(4)", fixedLength: true, maxLength: 4, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Label = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GrantGroupId = table.Column<int>(type: "int", nullable: true),
                    MaxUses = table.Column<int>(type: "int", nullable: false),
                    UseCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PausedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RevokedByUserId = table.Column<int>(type: "int", nullable: true),
                    SendCount = table.Column<int>(type: "int", nullable: false),
                    LastSentAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ResendWindowStartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ResendsInWindow = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invitations_permission_groups_GrantGroupId",
                        column: x => x.GrantGroupId,
                        principalTable: "permission_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "invitation_redemptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    InvitationId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RedeemedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invitation_redemptions_invitations_InvitationId",
                        column: x => x.InvitationId,
                        principalTable: "invitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_invitation_redemptions_InvitationId",
                table: "invitation_redemptions",
                column: "InvitationId");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_CodeHash",
                table: "invitations",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invitations_Email",
                table: "invitations",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_GrantGroupId",
                table: "invitations",
                column: "GrantGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_Kind_ExpiresAt",
                table: "invitations",
                columns: new[] { "Kind", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invitation_redemptions");

            migrationBuilder.DropTable(
                name: "invitations");

            migrationBuilder.DropColumn(
                name: "AdmittedAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "InvitedByUserId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "InvitedViaInvitationId",
                table: "users");
        }
    }
}
