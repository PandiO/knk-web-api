using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateMessageLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrivateMessageRetentionDays",
                table: "audit_log_retention_configurations",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.CreateTable(
                name: "private_message_log_entries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ClientMessageId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SenderUserId = table.Column<int>(type: "int", nullable: true),
                    SenderName = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecipientUserId = table.Column<int>(type: "int", nullable: true),
                    RecipientName = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Content = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Outcome = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ViaReply = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_private_message_log_entries_ClientMessageId",
                table: "private_message_log_entries",
                column: "ClientMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_private_message_log_entries_RecipientUserId_SentAt",
                table: "private_message_log_entries",
                columns: new[] { "RecipientUserId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_private_message_log_entries_SenderUserId_SentAt",
                table: "private_message_log_entries",
                columns: new[] { "SenderUserId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_private_message_log_entries_SentAt",
                table: "private_message_log_entries",
                column: "SentAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "private_message_log_entries");

            migrationBuilder.DropColumn(
                name: "PrivateMessageRetentionDays",
                table: "audit_log_retention_configurations");
        }
    }
}
