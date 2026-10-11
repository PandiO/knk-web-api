using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFirstJoinKitsGrantedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstJoinKitsGrantedAt",
                table: "users",
                type: "datetime(6)",
                nullable: true);

            // A player who already holds a claim on a first-join kit has had their first join
            // handled (KNG-81); mark them so the now-idempotent grant never repeats it for them.
            migrationBuilder.Sql(
                "UPDATE users u JOIN (" +
                "SELECT c.UserId, MIN(c.ClaimedAt) AS FirstClaimAt FROM kit_claims c " +
                "JOIN kits k ON k.Id = c.KitId WHERE k.GrantOnFirstJoin = 1 GROUP BY c.UserId" +
                ") f ON f.UserId = u.Id SET u.FirstJoinKitsGrantedAt = f.FirstClaimAt " +
                "WHERE u.FirstJoinKitsGrantedAt IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstJoinKitsGrantedAt",
                table: "users");
        }
    }
}
