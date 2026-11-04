using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// PandiO/knk-workspace#180 (world-task hardening, DESIGN §8.2 and §8.7): additive only. world_tasks gets the creator,
    /// claimer (uuid, user), expiry, lease, release count, row version, the draft link and AppliedAt, and the offer columns
    /// WT2 (#181) uses; FormSubmissionProgresses gets the draft's workflow session. Existing rows keep nulls: the sweep
    /// treats a missing ExpiresAt/LeaseUntil as "created/updated + the configured time".
    /// </summary>
    public partial class AddWorldTaskHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AppliedAt",
                table: "world_tasks",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimedByUserId",
                table: "world_tasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimedByUuid",
                table: "world_tasks",
                type: "varchar(36)",
                maxLength: 36,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "world_tasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "world_tasks",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FormSubmissionProgressId",
                table: "world_tasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseUntil",
                table: "world_tasks",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfferState",
                table: "world_tasks",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None",
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "OfferedAt",
                table: "world_tasks",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseCount",
                table: "world_tasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RowVersion",
                table: "world_tasks",
                type: "timestamp(6)",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetUserId",
                table: "world_tasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowSessionId",
                table: "FormSubmissionProgresses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_world_tasks_ClaimedByUserId",
                table: "world_tasks",
                column: "ClaimedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_world_tasks_CreatedByUserId",
                table: "world_tasks",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_world_tasks_FormSubmissionProgressId",
                table: "world_tasks",
                column: "FormSubmissionProgressId");

            migrationBuilder.CreateIndex(
                name: "IX_world_tasks_Status_ExpiresAt",
                table: "world_tasks",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_world_tasks_Status_LeaseUntil",
                table: "world_tasks",
                columns: new[] { "Status", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_world_tasks_TargetUserId",
                table: "world_tasks",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionProgresses_WorkflowSessionId",
                table: "FormSubmissionProgresses",
                column: "WorkflowSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_FormSubmissionProgresses_workflow_sessions_WorkflowSessionId",
                table: "FormSubmissionProgresses",
                column: "WorkflowSessionId",
                principalTable: "workflow_sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_world_tasks_FormSubmissionProgresses_FormSubmissionProgressId",
                table: "world_tasks",
                column: "FormSubmissionProgressId",
                principalTable: "FormSubmissionProgresses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_world_tasks_users_ClaimedByUserId",
                table: "world_tasks",
                column: "ClaimedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_world_tasks_users_CreatedByUserId",
                table: "world_tasks",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_world_tasks_users_TargetUserId",
                table: "world_tasks",
                column: "TargetUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FormSubmissionProgresses_workflow_sessions_WorkflowSessionId",
                table: "FormSubmissionProgresses");

            migrationBuilder.DropForeignKey(
                name: "FK_world_tasks_FormSubmissionProgresses_FormSubmissionProgressId",
                table: "world_tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_world_tasks_users_ClaimedByUserId",
                table: "world_tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_world_tasks_users_CreatedByUserId",
                table: "world_tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_world_tasks_users_TargetUserId",
                table: "world_tasks");

            migrationBuilder.DropIndex(
                name: "IX_world_tasks_ClaimedByUserId",
                table: "world_tasks");

            migrationBuilder.DropIndex(
                name: "IX_world_tasks_CreatedByUserId",
                table: "world_tasks");

            migrationBuilder.DropIndex(
                name: "IX_world_tasks_FormSubmissionProgressId",
                table: "world_tasks");

            migrationBuilder.DropIndex(
                name: "IX_world_tasks_Status_ExpiresAt",
                table: "world_tasks");

            migrationBuilder.DropIndex(
                name: "IX_world_tasks_Status_LeaseUntil",
                table: "world_tasks");

            migrationBuilder.DropIndex(
                name: "IX_world_tasks_TargetUserId",
                table: "world_tasks");

            migrationBuilder.DropIndex(
                name: "IX_FormSubmissionProgresses_WorkflowSessionId",
                table: "FormSubmissionProgresses");

            migrationBuilder.DropColumn(
                name: "AppliedAt",
                table: "world_tasks");

            migrationBuilder.DropColumn(
                name: "ClaimedByUserId",
                table: "world_tasks");

            migrationBuilder.DropColumn(
                name: "ClaimedByUuid",
                table: "world_tasks");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "world_tasks");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "world_tasks");

            migrationBuilder.DropColumn(
                name: "FormSubmissionProgressId",
                table: "world_tasks");

            migrationBuilder.DropColumn(
                name: "LeaseUntil",
                table: "world_tasks");

            migrationBuilder.DropColumn(
                name: "OfferState",
                table: "world_tasks");

            migrationBuilder.DropColumn(
                name: "OfferedAt",
                table: "world_tasks");

            migrationBuilder.DropColumn(
                name: "ReleaseCount",
                table: "world_tasks");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "world_tasks");

            migrationBuilder.DropColumn(
                name: "TargetUserId",
                table: "world_tasks");

            migrationBuilder.DropColumn(
                name: "WorkflowSessionId",
                table: "FormSubmissionProgresses");
        }
    }
}
