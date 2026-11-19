using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSiegeWeapons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveWeaponSpots",
                table: "siege_teams",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "FriendlyFire",
                table: "siege_scenarios",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WeaponSpotCooldownOverrideSeconds",
                table: "siege_scenarios",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "siege_ammo_types",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Effect = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Damage = table.Column<double>(type: "double", nullable: false),
                    StructureDamage = table.Column<double>(type: "double", nullable: false),
                    BlastRadius = table.Column<double>(type: "double", nullable: false),
                    Knockback = table.Column<double>(type: "double", nullable: false),
                    ArmorPiercing = table.Column<double>(type: "double", nullable: false),
                    ProjectileBlock = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ParticleTrail = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EffectRadius = table.Column<double>(type: "double", nullable: true),
                    EffectDurationSeconds = table.Column<double>(type: "double", nullable: true),
                    EffectLevel = table.Column<int>(type: "int", nullable: true),
                    PricePerShot = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_weapon_spots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    SiegeTeamId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinX = table.Column<int>(type: "int", nullable: true),
                    MinY = table.Column<int>(type: "int", nullable: true),
                    MinZ = table.Column<int>(type: "int", nullable: true),
                    MaxX = table.Column<int>(type: "int", nullable: true),
                    MaxY = table.Column<int>(type: "int", nullable: true),
                    MaxZ = table.Column<int>(type: "int", nullable: true),
                    WgRegionId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Facing = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SizeClass = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_weapon_spots_siege_scenarios_SiegeScenarioId",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_weapon_spots_siege_teams_SiegeTeamId",
                        column: x => x.SiegeTeamId,
                        principalTable: "siege_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_weapon_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SizeClass = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArmedSchematicId = table.Column<int>(type: "int", nullable: true),
                    FiredSchematicId = table.Column<int>(type: "int", nullable: true),
                    WreckSchematicId = table.Column<int>(type: "int", nullable: true),
                    MaxHealth = table.Column<int>(type: "int", nullable: false),
                    BuildTimeSeconds = table.Column<int>(type: "int", nullable: false),
                    ReloadSeconds = table.Column<int>(type: "int", nullable: false),
                    MaxRange = table.Column<double>(type: "double", nullable: false),
                    MinRange = table.Column<double>(type: "double", nullable: true),
                    StartSpreadRadius = table.Column<double>(type: "double", nullable: false),
                    MinSpreadRadius = table.Column<double>(type: "double", nullable: false),
                    RangingStep = table.Column<double>(type: "double", nullable: false),
                    FlightTimeAtMaxRangeSeconds = table.Column<double>(type: "double", nullable: false),
                    ArcHeight = table.Column<double>(type: "double", nullable: false),
                    LeaseIdleTimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    ConeHalfAngleDegrees = table.Column<double>(type: "double", nullable: false),
                    GateDamageMultiplier = table.Column<double>(type: "double", nullable: false),
                    WeaponDamageMultiplier = table.Column<double>(type: "double", nullable: false),
                    SpotCooldownSeconds = table.Column<int>(type: "int", nullable: false),
                    MatchPrice = table.Column<int>(type: "int", nullable: false),
                    UnlockPrice = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_weapon_template_ammo_types",
                columns: table => new
                {
                    SiegeWeaponTemplateId = table.Column<int>(type: "int", nullable: false),
                    SiegeAmmoTypeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_siege_weapon_template_ammo_types", x => new { x.SiegeWeaponTemplateId, x.SiegeAmmoTypeId });
                    table.ForeignKey(
                        name: "FK_siege_weapon_template_ammo_types_siege_ammo_types_SiegeAmmoT~",
                        column: x => x.SiegeAmmoTypeId,
                        principalTable: "siege_ammo_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_weapon_template_ammo_types_siege_weapon_templates_Sieg~",
                        column: x => x.SiegeWeaponTemplateId,
                        principalTable: "siege_weapon_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "user_siege_weapon_unlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SiegeWeaponTemplateId = table.Column<int>(type: "int", nullable: false),
                    UnlockedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_siege_weapon_unlocks_siege_weapon_templates_SiegeWeapon~",
                        column: x => x.SiegeWeaponTemplateId,
                        principalTable: "siege_weapon_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_siege_weapon_unlocks_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_siege_ammo_types_Name",
                table: "siege_ammo_types",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_weapon_spots_SiegeScenarioId_SortOrder",
                table: "siege_weapon_spots",
                columns: new[] { "SiegeScenarioId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_weapon_spots_SiegeTeamId",
                table: "siege_weapon_spots",
                column: "SiegeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_weapon_template_ammo_types_SiegeAmmoTypeId",
                table: "siege_weapon_template_ammo_types",
                column: "SiegeAmmoTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_weapon_templates_Name",
                table: "siege_weapon_templates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_siege_weapon_unlocks_SiegeWeaponTemplateId",
                table: "user_siege_weapon_unlocks",
                column: "SiegeWeaponTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_user_siege_weapon_unlocks_UserId_SiegeWeaponTemplateId",
                table: "user_siege_weapon_unlocks",
                columns: new[] { "UserId", "SiegeWeaponTemplateId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "siege_weapon_spots");

            migrationBuilder.DropTable(
                name: "siege_weapon_template_ammo_types");

            migrationBuilder.DropTable(
                name: "user_siege_weapon_unlocks");

            migrationBuilder.DropTable(
                name: "siege_ammo_types");

            migrationBuilder.DropTable(
                name: "siege_weapon_templates");

            migrationBuilder.DropColumn(
                name: "ActiveWeaponSpots",
                table: "siege_teams");

            migrationBuilder.DropColumn(
                name: "FriendlyFire",
                table: "siege_scenarios");

            migrationBuilder.DropColumn(
                name: "WeaponSpotCooldownOverrideSeconds",
                table: "siege_scenarios");
        }
    }
}
