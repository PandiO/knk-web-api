using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// GDPR deletion request flow (KNG-34, developer decisions 2026-10-03): player requests confirmed by
    /// email, staff filing, a grace period before execution. Additive; rows from before (all filed by
    /// the owner) become Source = Owner, and pending ones are scheduled GraceDays (5) after the request.
    /// </summary>
    public partial class AddDataDeletionRequestFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "privacy_deletion_requests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledByUserId",
                table: "privacy_deletion_requests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmationExpiresAt",
                table: "privacy_deletion_requests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationTokenHash",
                table: "privacy_deletion_requests",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "privacy_deletion_requests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledAt",
                table: "privacy_deletion_requests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Source",
                table: "privacy_deletion_requests",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)2);

            migrationBuilder.Sql(
                "UPDATE `privacy_deletion_requests` SET `ConfirmedAt` = `RequestedAt`, " +
                "`ScheduledAt` = DATE_ADD(`RequestedAt`, INTERVAL 5 DAY) WHERE `Status` = 0;");

            migrationBuilder.CreateIndex(
                name: "IX_privacy_deletion_requests_ConfirmationTokenHash",
                table: "privacy_deletion_requests",
                column: "ConfirmationTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_privacy_deletion_requests_Status_ScheduledAt",
                table: "privacy_deletion_requests",
                columns: new[] { "Status", "ScheduledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_privacy_deletion_requests_ConfirmationTokenHash",
                table: "privacy_deletion_requests");

            migrationBuilder.DropIndex(
                name: "IX_privacy_deletion_requests_Status_ScheduledAt",
                table: "privacy_deletion_requests");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "privacy_deletion_requests");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "privacy_deletion_requests");

            migrationBuilder.DropColumn(
                name: "ConfirmationExpiresAt",
                table: "privacy_deletion_requests");

            migrationBuilder.DropColumn(
                name: "ConfirmationTokenHash",
                table: "privacy_deletion_requests");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "privacy_deletion_requests");

            migrationBuilder.DropColumn(
                name: "ScheduledAt",
                table: "privacy_deletion_requests");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "privacy_deletion_requests");
        }
    }
}
