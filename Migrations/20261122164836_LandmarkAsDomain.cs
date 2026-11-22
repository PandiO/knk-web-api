using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Landmarks as a Domain subtype (landmark-domain DESIGN § 6.1, decisions LD10, LD11, LD16, PandiO/knk-workspace#983).
    /// Not additive (charter § 3.3): the existing landmark rows are dropped (LD10: only the dev database has any, #708 is
    /// not promoted), <c>landmarks</c> becomes the TPT child table of <c>domains</c> (keyed by the domain id; the name,
    /// pin, created time and region move to <c>domains</c>; <c>ExtentKind</c> becomes <c>AreaKind</c>, <c>DomainId</c>
    /// becomes <c>ParentDomainId</c>, <c>RegionId</c> and <c>Radius</c> go, <c>ShowEnterLeaveMessages</c> is new) and
    /// <c>domains.WgRegionId</c> becomes nullable (LD11). Down drops the landmark domains again and restores the #708 table
    /// (empty) and the NOT NULL region.
    /// </summary>
    public partial class LandmarkAsDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // LD10: drop the landmark rows first; their ids are not domain ids, so the table is rebuilt empty.
            migrationBuilder.Sql("DELETE FROM `landmarks`;");

            migrationBuilder.DropForeignKey(
                name: "FK_landmarks_domains_DomainId",
                table: "landmarks");

            migrationBuilder.DropForeignKey(
                name: "FK_landmarks_locations_LocationId",
                table: "landmarks");

            migrationBuilder.DropIndex(
                name: "IX_landmarks_LocationId",
                table: "landmarks");

            migrationBuilder.DropIndex(
                name: "IX_landmarks_Name",
                table: "landmarks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_landmarks_Preference_Range",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "Radius",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "landmarks");

            migrationBuilder.RenameColumn(
                name: "ExtentKind",
                table: "landmarks",
                newName: "AreaKind");

            migrationBuilder.RenameColumn(
                name: "DomainId",
                table: "landmarks",
                newName: "ParentDomainId");

            migrationBuilder.RenameIndex(
                name: "IX_landmarks_DomainId",
                table: "landmarks",
                newName: "IX_landmarks_ParentDomainId");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "landmarks",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .OldAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.AddColumn<bool>(
                name: "ShowEnterLeaveMessages",
                table: "landmarks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "WgRegionId",
                table: "domains",
                type: "longtext",
                nullable: true,
                collation: "utf8mb4_general_ci",
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.AddCheckConstraint(
                name: "CK_landmarks_Preference_Range",
                table: "landmarks",
                sql: "`Preference` >= 0.25 AND `Preference` <= 4");

            migrationBuilder.AddForeignKey(
                name: "FK_landmarks_domains_Id",
                table: "landmarks",
                column: "Id",
                principalTable: "domains",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_landmarks_domains_ParentDomainId",
                table: "landmarks",
                column: "ParentDomainId",
                principalTable: "domains",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The landmarks made on this model are domains: drop their landmark rows and their domain rows (sub-regions and
            // the other rows that cascade from a domain go with them), so the #708 table comes back empty, as Up left the
            // old one, and no domain is left without a region.
            migrationBuilder.Sql("CREATE TEMPORARY TABLE `landmark_domain_ids` AS SELECT `Id` FROM `landmarks`;");
            migrationBuilder.Sql("DELETE FROM `landmarks`;");
            migrationBuilder.Sql("DELETE FROM `domains` WHERE `Id` IN (SELECT `Id` FROM `landmark_domain_ids`);");
            migrationBuilder.Sql("DROP TEMPORARY TABLE `landmark_domain_ids`;");

            migrationBuilder.DropForeignKey(
                name: "FK_landmarks_domains_Id",
                table: "landmarks");

            migrationBuilder.DropForeignKey(
                name: "FK_landmarks_domains_ParentDomainId",
                table: "landmarks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_landmarks_Preference_Range",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "ShowEnterLeaveMessages",
                table: "landmarks");

            migrationBuilder.RenameColumn(
                name: "ParentDomainId",
                table: "landmarks",
                newName: "DomainId");

            migrationBuilder.RenameColumn(
                name: "AreaKind",
                table: "landmarks",
                newName: "ExtentKind");

            migrationBuilder.RenameIndex(
                name: "IX_landmarks_ParentDomainId",
                table: "landmarks",
                newName: "IX_landmarks_DomainId");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "landmarks",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "landmarks",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "landmarks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "landmarks",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "Radius",
                table: "landmarks",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegionId",
                table: "landmarks",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "domains",
                keyColumn: "WgRegionId",
                keyValue: null,
                column: "WgRegionId",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "WgRegionId",
                table: "domains",
                type: "longtext",
                nullable: false,
                collation: "utf8mb4_general_ci",
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_landmarks_LocationId",
                table: "landmarks",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_landmarks_Name",
                table: "landmarks",
                column: "Name");

            migrationBuilder.AddCheckConstraint(
                name: "CK_landmarks_Preference_Range",
                table: "landmarks",
                sql: "`Preference` >= 0.25 AND `Preference` <= 4");

            migrationBuilder.AddForeignKey(
                name: "FK_landmarks_domains_DomainId",
                table: "landmarks",
                column: "DomainId",
                principalTable: "domains",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_landmarks_locations_LocationId",
                table: "landmarks",
                column: "LocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
