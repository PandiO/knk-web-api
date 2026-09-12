using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    // Item 5 of docs/features/gate-structure-animation/GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md:
    // introduces GateDoor to support multiple independently-animating doors per GateStructure.
    // Hand-edited after scaffolding (`dotnet ef migrations add AddGateDoor`) to add the data
    // migration the plan calls for (item 5.1): auto-create exactly one GateDoor per existing
    // GateStructure, carrying over its current geometry/animation/state data, rather than the
    // scaffolded drop-and-recreate that would have silently discarded it. The scaffolder also
    // mis-detected two unrelated nullable-int columns (RightDoorSeedBlockId, ReferencePoint2Id)
    // as being "renamed" into two of the new override columns purely because their types
    // matched - replaced with straightforward drop + add. FaceDirection also changes storage
    // format here: the legacy column held free-form lowercase-hyphenated strings ("north-east");
    // the new GateFaceDirection enum persists as its upper-snake-case member name ("NORTH_EAST"),
    // so the data copy remaps old values to new via CASE rather than a straight passthrough.
    //
    // BACK UP THE DATABASE BEFORE APPLYING THIS MIGRATION - it restructures every existing gate
    // row's field ownership across two tables. See the plan doc's cross-cutting notes.
    public partial class AddGateDoor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The live dev DB has drifted from what this migration's scaffolded DropForeignKey/
            // DropIndex calls assumed (confirmed by inspecting information_schema directly): none of
            // the location/material-ref foreign keys or their indexes below actually exist on
            // gate_structures/gate_block_snapshots/gate_opened_block_snapshots in this environment,
            // even though the columns and EF's migration history say they should. Guard every drop
            // on an information_schema existence check so this migration is idempotent/portable
            // across a fresh DB (where these constraints genuinely exist) and this drifted one alike,
            // rather than assuming either state.
            migrationBuilder.Sql(@"
                SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_block_snapshots' AND CONSTRAINT_NAME = 'FK_gate_block_snapshots_gate_structures_GateStructureId' AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@fk_exists > 0, 'ALTER TABLE `gate_block_snapshots` DROP FOREIGN KEY `FK_gate_block_snapshots_gate_structures_GateStructureId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_opened_block_snapshots' AND CONSTRAINT_NAME = 'FK_gate_opened_block_snapshots_gate_structures_GateStructureId' AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@fk_exists > 0, 'ALTER TABLE `gate_opened_block_snapshots` DROP FOREIGN KEY `FK_gate_opened_block_snapshots_gate_structures_GateStructureId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND CONSTRAINT_NAME = 'FK_gate_structures_locations_AnchorPointId' AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@fk_exists > 0, 'ALTER TABLE `gate_structures` DROP FOREIGN KEY `FK_gate_structures_locations_AnchorPointId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND CONSTRAINT_NAME = 'FK_gate_structures_locations_HingeAxisId' AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@fk_exists > 0, 'ALTER TABLE `gate_structures` DROP FOREIGN KEY `FK_gate_structures_locations_HingeAxisId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND CONSTRAINT_NAME = 'FK_gate_structures_locations_InfoDisplayLocationId' AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@fk_exists > 0, 'ALTER TABLE `gate_structures` DROP FOREIGN KEY `FK_gate_structures_locations_InfoDisplayLocationId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND CONSTRAINT_NAME = 'FK_gate_structures_locations_LeftDoorSeedBlockId' AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@fk_exists > 0, 'ALTER TABLE `gate_structures` DROP FOREIGN KEY `FK_gate_structures_locations_LeftDoorSeedBlockId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND CONSTRAINT_NAME = 'FK_gate_structures_locations_OpenAnchorPointId' AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@fk_exists > 0, 'ALTER TABLE `gate_structures` DROP FOREIGN KEY `FK_gate_structures_locations_OpenAnchorPointId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND CONSTRAINT_NAME = 'FK_gate_structures_locations_ReferencePoint1Id' AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@fk_exists > 0, 'ALTER TABLE `gate_structures` DROP FOREIGN KEY `FK_gate_structures_locations_ReferencePoint1Id`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND CONSTRAINT_NAME = 'FK_gate_structures_locations_ReferencePoint2Id' AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@fk_exists > 0, 'ALTER TABLE `gate_structures` DROP FOREIGN KEY `FK_gate_structures_locations_ReferencePoint2Id`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND CONSTRAINT_NAME = 'FK_gate_structures_locations_RightDoorSeedBlockId' AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@fk_exists > 0, 'ALTER TABLE `gate_structures` DROP FOREIGN KEY `FK_gate_structures_locations_RightDoorSeedBlockId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @fk_exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND CONSTRAINT_NAME = 'FK_gate_structures_minecraftmaterialrefs_FallbackMaterialRefId' AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@fk_exists > 0, 'ALTER TABLE `gate_structures` DROP FOREIGN KEY `FK_gate_structures_minecraftmaterialrefs_FallbackMaterialRefId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND INDEX_NAME = 'IX_GateStructure_GateType');
                SET @sql := IF(@idx_exists > 0, 'ALTER TABLE `gate_structures` DROP INDEX `IX_GateStructure_GateType`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND INDEX_NAME = 'IX_GateStructure_IsActive');
                SET @sql := IF(@idx_exists > 0, 'ALTER TABLE `gate_structures` DROP INDEX `IX_GateStructure_IsActive`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND INDEX_NAME = 'IX_GateStructure_IsOpened');
                SET @sql := IF(@idx_exists > 0, 'ALTER TABLE `gate_structures` DROP INDEX `IX_GateStructure_IsOpened`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND INDEX_NAME = 'IX_gate_structures_AnchorPointId');
                SET @sql := IF(@idx_exists > 0, 'ALTER TABLE `gate_structures` DROP INDEX `IX_gate_structures_AnchorPointId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND INDEX_NAME = 'IX_gate_structures_FallbackMaterialRefId');
                SET @sql := IF(@idx_exists > 0, 'ALTER TABLE `gate_structures` DROP INDEX `IX_gate_structures_FallbackMaterialRefId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND INDEX_NAME = 'IX_gate_structures_HingeAxisId');
                SET @sql := IF(@idx_exists > 0, 'ALTER TABLE `gate_structures` DROP INDEX `IX_gate_structures_HingeAxisId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND INDEX_NAME = 'IX_gate_structures_InfoDisplayLocationId');
                SET @sql := IF(@idx_exists > 0, 'ALTER TABLE `gate_structures` DROP INDEX `IX_gate_structures_InfoDisplayLocationId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND INDEX_NAME = 'IX_gate_structures_LeftDoorSeedBlockId');
                SET @sql := IF(@idx_exists > 0, 'ALTER TABLE `gate_structures` DROP INDEX `IX_gate_structures_LeftDoorSeedBlockId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND INDEX_NAME = 'IX_gate_structures_OpenAnchorPointId');
                SET @sql := IF(@idx_exists > 0, 'ALTER TABLE `gate_structures` DROP INDEX `IX_gate_structures_OpenAnchorPointId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND INDEX_NAME = 'IX_gate_structures_ReferencePoint1Id');
                SET @sql := IF(@idx_exists > 0, 'ALTER TABLE `gate_structures` DROP INDEX `IX_gate_structures_ReferencePoint1Id`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND INDEX_NAME = 'IX_gate_structures_ReferencePoint2Id');
                SET @sql := IF(@idx_exists > 0, 'ALTER TABLE `gate_structures` DROP INDEX `IX_gate_structures_ReferencePoint2Id`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

                SET @idx_exists := (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'gate_structures' AND INDEX_NAME = 'IX_gate_structures_RightDoorSeedBlockId');
                SET @sql := IF(@idx_exists > 0, 'ALTER TABLE `gate_structures` DROP INDEX `IX_gate_structures_RightDoorSeedBlockId`', 'DO 0');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;

            ");

            // --- Create gate_doors BEFORE dropping the per-door columns from gate_structures,
            // so the data migration below can copy every existing structure's current values
            // into its new auto-created door. Each door's Id is set explicitly equal to its
            // parent structure's Id (MySQL allows explicit values in AUTO_INCREMENT columns),
            // so the later rename of gate_block_snapshots/gate_opened_block_snapshots
            // .GateStructureId -> GateDoorId needs no separate row-by-row remap: the existing FK
            // values already point at the matching door row. ---
            migrationBuilder.CreateTable(
                name: "gate_doors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GateStructureId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HealthCurrent = table.Column<double>(type: "double", nullable: false),
                    HealthMax = table.Column<double>(type: "double", nullable: false),
                    RespawnRateSeconds = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CanRespawn = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsDestroyed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsInvincible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    OpenedState = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GateType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GeometryDefinitionMode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MotionType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AnimationDurationTicks = table.Column<int>(type: "int", nullable: false),
                    AnimationTickRate = table.Column<int>(type: "int", nullable: false),
                    FaceDirection = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AnchorPointId = table.Column<int>(type: "int", nullable: true),
                    OpenAnchorPointId = table.Column<int>(type: "int", nullable: true),
                    ReferencePoint1Id = table.Column<int>(type: "int", nullable: true),
                    ReferencePoint2Id = table.Column<int>(type: "int", nullable: true),
                    GeometryWidth = table.Column<int>(type: "int", nullable: false),
                    GeometryHeight = table.Column<int>(type: "int", nullable: false),
                    GeometryDepth = table.Column<int>(type: "int", nullable: false),
                    MotionDistanceBlocks = table.Column<int>(type: "int", nullable: false),
                    ClipToGeometryBounds = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SeedBlocks = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScanMaxBlocks = table.Column<int>(type: "int", nullable: false),
                    ScanMaxRadius = table.Column<int>(type: "int", nullable: false),
                    ScanMaterialWhitelist = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScanMaterialBlacklist = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScanPlaneConstraint = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FallbackMaterialRefId = table.Column<int>(type: "int", nullable: true),
                    TileEntityPolicy = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RotationMaxAngleDegrees = table.Column<int>(type: "int", nullable: false),
                    HingeAxisId = table.Column<int>(type: "int", nullable: true),
                    MirrorRotation = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LeftDoorSeedBlockId = table.Column<int>(type: "int", nullable: true),
                    RightDoorSeedBlockId = table.Column<int>(type: "int", nullable: true),
                    RegionClosedId = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RegionOpenedId = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AllowPassThrough = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PassThroughDurationSeconds = table.Column<int>(type: "int", nullable: false),
                    PassThroughConditionsJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InfoDisplayLocationId = table.Column<int>(type: "int", nullable: true),
                    ShowHealthDisplay = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    HealthDisplayMode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HealthDisplayYOffset = table.Column<int>(type: "int", nullable: false),
                    GateNameDisplayMode = table.Column<int>(type: "int", nullable: false),
                    StatusDisplayMode = table.Column<int>(type: "int", nullable: false),
                    DoorNameDisplayMode = table.Column<int>(type: "int", nullable: false),
                    AllowContinuousDamage = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ContinuousDamageMultiplier = table.Column<double>(type: "double", nullable: false),
                    ContinuousDamageDurationSeconds = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gate_doors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gate_doors_gate_structures_GateStructureId",
                        column: x => x.GateStructureId,
                        principalTable: "gate_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gate_doors_locations_AnchorPointId",
                        column: x => x.AnchorPointId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gate_doors_locations_HingeAxisId",
                        column: x => x.HingeAxisId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gate_doors_locations_InfoDisplayLocationId",
                        column: x => x.InfoDisplayLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gate_doors_locations_LeftDoorSeedBlockId",
                        column: x => x.LeftDoorSeedBlockId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gate_doors_locations_OpenAnchorPointId",
                        column: x => x.OpenAnchorPointId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gate_doors_locations_ReferencePoint1Id",
                        column: x => x.ReferencePoint1Id,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gate_doors_locations_ReferencePoint2Id",
                        column: x => x.ReferencePoint2Id,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gate_doors_locations_RightDoorSeedBlockId",
                        column: x => x.RightDoorSeedBlockId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gate_doors_minecraftmaterialrefs_FallbackMaterialRefId",
                        column: x => x.FallbackMaterialRefId,
                        principalTable: "minecraftmaterialrefs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_gate_doors_AnchorPointId",
                table: "gate_doors",
                column: "AnchorPointId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_doors_FallbackMaterialRefId",
                table: "gate_doors",
                column: "FallbackMaterialRefId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_doors_HingeAxisId",
                table: "gate_doors",
                column: "HingeAxisId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_doors_InfoDisplayLocationId",
                table: "gate_doors",
                column: "InfoDisplayLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_doors_LeftDoorSeedBlockId",
                table: "gate_doors",
                column: "LeftDoorSeedBlockId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_doors_OpenAnchorPointId",
                table: "gate_doors",
                column: "OpenAnchorPointId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_doors_ReferencePoint1Id",
                table: "gate_doors",
                column: "ReferencePoint1Id");

            migrationBuilder.CreateIndex(
                name: "IX_gate_doors_ReferencePoint2Id",
                table: "gate_doors",
                column: "ReferencePoint2Id");

            migrationBuilder.CreateIndex(
                name: "IX_gate_doors_RightDoorSeedBlockId",
                table: "gate_doors",
                column: "RightDoorSeedBlockId");

            migrationBuilder.CreateIndex(
                name: "IX_GateDoor_GateStructureId_Name",
                table: "gate_doors",
                columns: new[] { "GateStructureId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GateDoor_GateType",
                table: "gate_doors",
                column: "GateType");

            migrationBuilder.CreateIndex(
                name: "IX_GateDoor_IsActive",
                table: "gate_doors",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_GateDoor_OpenedState",
                table: "gate_doors",
                column: "OpenedState");

            // --- Data migration (plan item 5.1): auto-create exactly one GateDoor per existing
            // GateStructure, carrying over its current geometry/animation/state data. The door's
            // Id is set explicitly equal to its parent structure's Id so the snapshot-table FK
            // rename below needs no separate row-by-row remap. IsOpened/IsJammed collapse into
            // the single OpenedState enum (decision 5.0-C): JAMMED wins if set, otherwise OPEN
            // if IsOpened, otherwise CLOSED. GateType/GeometryDefinitionMode/MotionType/
            // TileEntityPolicy/HealthDisplayMode are stored as strings on both sides, so they
            // copy straight across; GateNameDisplayMode/StatusDisplayMode are plain ints on both
            // sides for the same reason. FaceDirection changes format from a free-form lowercase
            // -hyphenated string ("north-east") to the GateFaceDirection enum's upper-snake-case
            // member name ("NORTH_EAST"), so it's remapped via CASE rather than copied straight.
            // DoorNameDisplayMode (new field, no source column) defaults to 0
            // (GateInfoDisplayMode.ALWAYS). The door's Name defaults to the structure's own name
            // (safe pre-migration since a structure has exactly one door at this point, so no
            // name collision within the structure is possible yet). ---
            migrationBuilder.Sql(@"
                INSERT INTO gate_doors (
                    Id, GateStructureId, Name,
                    HealthCurrent, HealthMax, RespawnRateSeconds,
                    IsActive, CanRespawn, IsDestroyed, IsInvincible, OpenedState,
                    GateType, GeometryDefinitionMode, MotionType, AnimationDurationTicks, AnimationTickRate, FaceDirection,
                    AnchorPointId, OpenAnchorPointId, ReferencePoint1Id, ReferencePoint2Id,
                    GeometryWidth, GeometryHeight, GeometryDepth, MotionDistanceBlocks, ClipToGeometryBounds,
                    SeedBlocks, ScanMaxBlocks, ScanMaxRadius, ScanMaterialWhitelist, ScanMaterialBlacklist, ScanPlaneConstraint,
                    FallbackMaterialRefId, TileEntityPolicy,
                    RotationMaxAngleDegrees, HingeAxisId, MirrorRotation, LeftDoorSeedBlockId, RightDoorSeedBlockId,
                    RegionClosedId, RegionOpenedId,
                    AllowPassThrough, PassThroughDurationSeconds, PassThroughConditionsJson,
                    InfoDisplayLocationId, ShowHealthDisplay, HealthDisplayMode, HealthDisplayYOffset,
                    GateNameDisplayMode, StatusDisplayMode, DoorNameDisplayMode,
                    AllowContinuousDamage, ContinuousDamageMultiplier, ContinuousDamageDurationSeconds
                )
                SELECT
                    gs.Id, gs.Id, d.Name,
                    gs.HealthCurrent, gs.HealthMax, gs.RespawnRateSeconds,
                    gs.IsActive, gs.CanRespawn, gs.IsDestroyed, gs.IsInvincible,
                    CASE WHEN gs.IsJammed = 1 THEN 'JAMMED' WHEN gs.IsOpened = 1 THEN 'OPEN' ELSE 'CLOSED' END,
                    gs.GateType, gs.GeometryDefinitionMode, gs.MotionType, gs.AnimationDurationTicks, gs.AnimationTickRate,
                    CASE gs.FaceDirection
                        WHEN 'north' THEN 'NORTH'
                        WHEN 'north-east' THEN 'NORTH_EAST'
                        WHEN 'east' THEN 'EAST'
                        WHEN 'south-east' THEN 'SOUTH_EAST'
                        WHEN 'south' THEN 'SOUTH'
                        WHEN 'south-west' THEN 'SOUTH_WEST'
                        WHEN 'west' THEN 'WEST'
                        WHEN 'north-west' THEN 'NORTH_WEST'
                        ELSE 'NORTH'
                    END,
                    gs.AnchorPointId, gs.OpenAnchorPointId, gs.ReferencePoint1Id, gs.ReferencePoint2Id,
                    gs.GeometryWidth, gs.GeometryHeight, gs.GeometryDepth, gs.MotionDistanceBlocks, gs.ClipToGeometryBounds,
                    gs.SeedBlocks, gs.ScanMaxBlocks, gs.ScanMaxRadius, gs.ScanMaterialWhitelist, gs.ScanMaterialBlacklist, gs.ScanPlaneConstraint,
                    gs.FallbackMaterialRefId, gs.TileEntityPolicy,
                    gs.RotationMaxAngleDegrees, gs.HingeAxisId, gs.MirrorRotation, gs.LeftDoorSeedBlockId, gs.RightDoorSeedBlockId,
                    gs.RegionClosedId, gs.RegionOpenedId,
                    gs.AllowPassThrough, gs.PassThroughDurationSeconds, gs.PassThroughConditionsJson,
                    gs.InfoDisplayLocationId, gs.ShowHealthDisplay, gs.HealthDisplayMode, gs.HealthDisplayYOffset,
                    gs.GateNameDisplayMode, gs.StatusDisplayMode, 0,
                    gs.AllowContinuousDamage, gs.ContinuousDamageMultiplier, gs.ContinuousDamageDurationSeconds
                FROM gate_structures gs
                JOIN domains d ON d.Id = gs.Id;
            ");

            // Now safe to drop the per-door columns from gate_structures - their data has been
            // copied into gate_doors above.
            migrationBuilder.DropColumn(name: "AllowContinuousDamage", table: "gate_structures");
            migrationBuilder.DropColumn(name: "AllowPassThrough", table: "gate_structures");
            migrationBuilder.DropColumn(name: "AnchorPointId", table: "gate_structures");
            migrationBuilder.DropColumn(name: "AnimationDurationTicks", table: "gate_structures");
            migrationBuilder.DropColumn(name: "AnimationTickRate", table: "gate_structures");
            migrationBuilder.DropColumn(name: "CanRespawn", table: "gate_structures");
            migrationBuilder.DropColumn(name: "ClipToGeometryBounds", table: "gate_structures");
            migrationBuilder.DropColumn(name: "ContinuousDamageDurationSeconds", table: "gate_structures");
            migrationBuilder.DropColumn(name: "ContinuousDamageMultiplier", table: "gate_structures");
            migrationBuilder.DropColumn(name: "FaceDirection", table: "gate_structures");
            migrationBuilder.DropColumn(name: "FallbackMaterialRefId", table: "gate_structures");
            migrationBuilder.DropColumn(name: "GateNameDisplayMode", table: "gate_structures");
            migrationBuilder.DropColumn(name: "GateType", table: "gate_structures");
            migrationBuilder.DropColumn(name: "GeometryDefinitionMode", table: "gate_structures");
            migrationBuilder.DropColumn(name: "GeometryDepth", table: "gate_structures");
            migrationBuilder.DropColumn(name: "GeometryHeight", table: "gate_structures");
            migrationBuilder.DropColumn(name: "GeometryWidth", table: "gate_structures");
            migrationBuilder.DropColumn(name: "HealthCurrent", table: "gate_structures");
            migrationBuilder.DropColumn(name: "HealthDisplayMode", table: "gate_structures");
            migrationBuilder.DropColumn(name: "HealthDisplayYOffset", table: "gate_structures");
            migrationBuilder.DropColumn(name: "HealthMax", table: "gate_structures");
            migrationBuilder.DropColumn(name: "HingeAxisId", table: "gate_structures");
            migrationBuilder.DropColumn(name: "InfoDisplayLocationId", table: "gate_structures");
            migrationBuilder.DropColumn(name: "IsActive", table: "gate_structures");
            migrationBuilder.DropColumn(name: "IsDestroyed", table: "gate_structures");
            migrationBuilder.DropColumn(name: "IsInvincible", table: "gate_structures");
            migrationBuilder.DropColumn(name: "IsJammed", table: "gate_structures");
            migrationBuilder.DropColumn(name: "IsOpened", table: "gate_structures");
            migrationBuilder.DropColumn(name: "LeftDoorSeedBlockId", table: "gate_structures");
            migrationBuilder.DropColumn(name: "MirrorRotation", table: "gate_structures");
            migrationBuilder.DropColumn(name: "MotionDistanceBlocks", table: "gate_structures");
            migrationBuilder.DropColumn(name: "MotionType", table: "gate_structures");
            migrationBuilder.DropColumn(name: "OpenAnchorPointId", table: "gate_structures");
            migrationBuilder.DropColumn(name: "PassThroughConditionsJson", table: "gate_structures");
            migrationBuilder.DropColumn(name: "PassThroughDurationSeconds", table: "gate_structures");
            migrationBuilder.DropColumn(name: "ReferencePoint1Id", table: "gate_structures");
            migrationBuilder.DropColumn(name: "ReferencePoint2Id", table: "gate_structures");
            migrationBuilder.DropColumn(name: "RegionClosedId", table: "gate_structures");
            migrationBuilder.DropColumn(name: "RegionOpenedId", table: "gate_structures");
            migrationBuilder.DropColumn(name: "RespawnRateSeconds", table: "gate_structures");
            migrationBuilder.DropColumn(name: "RightDoorSeedBlockId", table: "gate_structures");
            migrationBuilder.DropColumn(name: "RotationMaxAngleDegrees", table: "gate_structures");
            migrationBuilder.DropColumn(name: "ScanMaterialBlacklist", table: "gate_structures");
            migrationBuilder.DropColumn(name: "ScanMaterialWhitelist", table: "gate_structures");
            migrationBuilder.DropColumn(name: "ScanMaxBlocks", table: "gate_structures");
            migrationBuilder.DropColumn(name: "ScanMaxRadius", table: "gate_structures");
            migrationBuilder.DropColumn(name: "ScanPlaneConstraint", table: "gate_structures");
            migrationBuilder.DropColumn(name: "SeedBlocks", table: "gate_structures");
            migrationBuilder.DropColumn(name: "ShowHealthDisplay", table: "gate_structures");
            migrationBuilder.DropColumn(name: "StatusDisplayMode", table: "gate_structures");
            migrationBuilder.DropColumn(name: "TileEntityPolicy", table: "gate_structures");

            // Structure-level cascading override columns (decision 5.0-B) - all nullable, added
            // fresh rather than repurposing old columns.
            migrationBuilder.AddColumn<bool>(
                name: "AllowContinuousDamageOverride",
                table: "gate_structures",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowPassThroughOverride",
                table: "gate_structures",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanRespawnOverride",
                table: "gate_structures",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ContinuousDamageMultiplierOverride",
                table: "gate_structures",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GateNameDisplayModeOverride",
                table: "gate_structures",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "HealthDisplayModeOverride",
                table: "gate_structures",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "HealthDisplayYOffsetOverride",
                table: "gate_structures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActiveOverride",
                table: "gate_structures",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDestroyedOverride",
                table: "gate_structures",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvincibleOverride",
                table: "gate_structures",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpenedStateOverride",
                table: "gate_structures",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "PassThroughDurationSecondsOverride",
                table: "gate_structures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowHealthDisplayOverride",
                table: "gate_structures",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusDisplayModeOverride",
                table: "gate_structures",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            // Retarget block-snapshot ownership from GateStructure to GateDoor (a scan targets
            // one door, not a whole structure). Existing FK values already point at the correct
            // door row since each door's Id was set equal to its parent structure's Id above.
            migrationBuilder.RenameColumn(
                name: "GateStructureId",
                table: "gate_opened_block_snapshots",
                newName: "GateDoorId");

            migrationBuilder.RenameIndex(
                name: "IX_GateOpenedBlockSnapshot_GateStructureId",
                table: "gate_opened_block_snapshots",
                newName: "IX_GateOpenedBlockSnapshot_GateDoorId");

            migrationBuilder.RenameIndex(
                name: "IX_GateOpenedBlockSnapshot_GateId_SortOrder",
                table: "gate_opened_block_snapshots",
                newName: "IX_GateOpenedBlockSnapshot_GateDoorId_SortOrder");

            migrationBuilder.RenameColumn(
                name: "GateStructureId",
                table: "gate_block_snapshots",
                newName: "GateDoorId");

            migrationBuilder.RenameIndex(
                name: "IX_GateBlockSnapshot_GateStructureId",
                table: "gate_block_snapshots",
                newName: "IX_GateBlockSnapshot_GateDoorId");

            migrationBuilder.RenameIndex(
                name: "IX_GateBlockSnapshot_GateId_SortOrder",
                table: "gate_block_snapshots",
                newName: "IX_GateBlockSnapshot_GateDoorId_SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_gate_block_snapshots_gate_doors_GateDoorId",
                table: "gate_block_snapshots",
                column: "GateDoorId",
                principalTable: "gate_doors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_gate_opened_block_snapshots_gate_doors_GateDoorId",
                table: "gate_opened_block_snapshots",
                column: "GateDoorId",
                principalTable: "gate_doors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_gate_block_snapshots_gate_doors_GateDoorId",
                table: "gate_block_snapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_gate_opened_block_snapshots_gate_doors_GateDoorId",
                table: "gate_opened_block_snapshots");

            // Re-add the old gate_structures columns before copying door data back into them.
            migrationBuilder.AddColumn<bool>(name: "AllowContinuousDamage", table: "gate_structures", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "AllowPassThrough", table: "gate_structures", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<int>(name: "AnchorPointId", table: "gate_structures", type: "int", nullable: true);
            migrationBuilder.AddColumn<int>(name: "AnimationDurationTicks", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "AnimationTickRate", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<bool>(name: "CanRespawn", table: "gate_structures", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "ClipToGeometryBounds", table: "gate_structures", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<int>(name: "ContinuousDamageDurationSeconds", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<double>(name: "ContinuousDamageMultiplier", table: "gate_structures", type: "double", nullable: false, defaultValue: 0.0);
            migrationBuilder.AddColumn<string>(name: "FaceDirection", table: "gate_structures", type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<int>(name: "FallbackMaterialRefId", table: "gate_structures", type: "int", nullable: true);
            migrationBuilder.AddColumn<int>(name: "GateNameDisplayMode", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "GateType", table: "gate_structures", type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "SLIDING", collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<string>(name: "GeometryDefinitionMode", table: "gate_structures", type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "PLANE_GRID", collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<int>(name: "GeometryDepth", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "GeometryHeight", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "GeometryWidth", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<double>(name: "HealthCurrent", table: "gate_structures", type: "double", nullable: false, defaultValue: 0.0);
            migrationBuilder.AddColumn<string>(name: "HealthDisplayMode", table: "gate_structures", type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "ALWAYS", collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<int>(name: "HealthDisplayYOffset", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<double>(name: "HealthMax", table: "gate_structures", type: "double", nullable: false, defaultValue: 0.0);
            migrationBuilder.AddColumn<int>(name: "HingeAxisId", table: "gate_structures", type: "int", nullable: true);
            migrationBuilder.AddColumn<int>(name: "InfoDisplayLocationId", table: "gate_structures", type: "int", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "IsActive", table: "gate_structures", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "IsDestroyed", table: "gate_structures", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "IsInvincible", table: "gate_structures", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "IsJammed", table: "gate_structures", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "IsOpened", table: "gate_structures", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<int>(name: "LeftDoorSeedBlockId", table: "gate_structures", type: "int", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "MirrorRotation", table: "gate_structures", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<int>(name: "MotionDistanceBlocks", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "MotionType", table: "gate_structures", type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "VERTICAL", collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<int>(name: "OpenAnchorPointId", table: "gate_structures", type: "int", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PassThroughConditionsJson", table: "gate_structures", type: "varchar(2000)", maxLength: 2000, nullable: false, defaultValue: "", collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<int>(name: "PassThroughDurationSeconds", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "ReferencePoint1Id", table: "gate_structures", type: "int", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ReferencePoint2Id", table: "gate_structures", type: "int", nullable: true);
            migrationBuilder.AddColumn<string>(name: "RegionClosedId", table: "gate_structures", type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<string>(name: "RegionOpenedId", table: "gate_structures", type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<int>(name: "RespawnRateSeconds", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "RightDoorSeedBlockId", table: "gate_structures", type: "int", nullable: true);
            migrationBuilder.AddColumn<int>(name: "RotationMaxAngleDegrees", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "ScanMaterialBlacklist", table: "gate_structures", type: "varchar(1000)", maxLength: 1000, nullable: false, defaultValue: "", collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<string>(name: "ScanMaterialWhitelist", table: "gate_structures", type: "varchar(1000)", maxLength: 1000, nullable: false, defaultValue: "", collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<int>(name: "ScanMaxBlocks", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "ScanMaxRadius", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<bool>(name: "ScanPlaneConstraint", table: "gate_structures", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "SeedBlocks", table: "gate_structures", type: "varchar(2000)", maxLength: 2000, nullable: false, defaultValue: "", collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<bool>(name: "ShowHealthDisplay", table: "gate_structures", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<int>(name: "StatusDisplayMode", table: "gate_structures", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "TileEntityPolicy", table: "gate_structures", type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "DECORATIVE_ONLY", collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            // Best-effort data restoration: copy each structure's FIRST door (lowest Id) back
            // onto the recreated columns. If multiple doors were created after the forward
            // migration, all but the first are silently dropped from gate_structures - this is
            // a structural rollback for local dev iteration, not a data-safe production restore
            // (use the pre-migration DB backup for that - see
            // GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md item 5.1). FaceDirection is remapped back
            // from the enum's upper-snake-case member name to the legacy lowercase-hyphenated
            // string.
            migrationBuilder.Sql(@"
                UPDATE gate_structures gs
                JOIN gate_doors gd ON gd.Id = (
                    SELECT MIN(gd2.Id) FROM gate_doors gd2 WHERE gd2.GateStructureId = gs.Id
                )
                SET
                    gs.AllowContinuousDamage = gd.AllowContinuousDamage,
                    gs.AllowPassThrough = gd.AllowPassThrough,
                    gs.AnchorPointId = gd.AnchorPointId,
                    gs.AnimationDurationTicks = gd.AnimationDurationTicks,
                    gs.AnimationTickRate = gd.AnimationTickRate,
                    gs.CanRespawn = gd.CanRespawn,
                    gs.ClipToGeometryBounds = gd.ClipToGeometryBounds,
                    gs.ContinuousDamageDurationSeconds = gd.ContinuousDamageDurationSeconds,
                    gs.ContinuousDamageMultiplier = gd.ContinuousDamageMultiplier,
                    gs.FaceDirection = CASE gd.FaceDirection
                        WHEN 'NORTH' THEN 'north'
                        WHEN 'NORTH_EAST' THEN 'north-east'
                        WHEN 'EAST' THEN 'east'
                        WHEN 'SOUTH_EAST' THEN 'south-east'
                        WHEN 'SOUTH' THEN 'south'
                        WHEN 'SOUTH_WEST' THEN 'south-west'
                        WHEN 'WEST' THEN 'west'
                        WHEN 'NORTH_WEST' THEN 'north-west'
                        ELSE 'north'
                    END,
                    gs.FallbackMaterialRefId = gd.FallbackMaterialRefId,
                    gs.GateNameDisplayMode = gd.GateNameDisplayMode,
                    gs.GateType = gd.GateType,
                    gs.GeometryDefinitionMode = gd.GeometryDefinitionMode,
                    gs.GeometryDepth = gd.GeometryDepth,
                    gs.GeometryHeight = gd.GeometryHeight,
                    gs.GeometryWidth = gd.GeometryWidth,
                    gs.HealthCurrent = gd.HealthCurrent,
                    gs.HealthDisplayMode = gd.HealthDisplayMode,
                    gs.HealthDisplayYOffset = gd.HealthDisplayYOffset,
                    gs.HealthMax = gd.HealthMax,
                    gs.HingeAxisId = gd.HingeAxisId,
                    gs.InfoDisplayLocationId = gd.InfoDisplayLocationId,
                    gs.IsActive = gd.IsActive,
                    gs.IsDestroyed = gd.IsDestroyed,
                    gs.IsInvincible = gd.IsInvincible,
                    gs.IsJammed = CASE WHEN gd.OpenedState = 'JAMMED' THEN 1 ELSE 0 END,
                    gs.IsOpened = CASE WHEN gd.OpenedState = 'OPEN' THEN 1 ELSE 0 END,
                    gs.LeftDoorSeedBlockId = gd.LeftDoorSeedBlockId,
                    gs.MirrorRotation = gd.MirrorRotation,
                    gs.MotionDistanceBlocks = gd.MotionDistanceBlocks,
                    gs.MotionType = gd.MotionType,
                    gs.OpenAnchorPointId = gd.OpenAnchorPointId,
                    gs.PassThroughConditionsJson = gd.PassThroughConditionsJson,
                    gs.PassThroughDurationSeconds = gd.PassThroughDurationSeconds,
                    gs.ReferencePoint1Id = gd.ReferencePoint1Id,
                    gs.ReferencePoint2Id = gd.ReferencePoint2Id,
                    gs.RegionClosedId = gd.RegionClosedId,
                    gs.RegionOpenedId = gd.RegionOpenedId,
                    gs.RespawnRateSeconds = gd.RespawnRateSeconds,
                    gs.RightDoorSeedBlockId = gd.RightDoorSeedBlockId,
                    gs.RotationMaxAngleDegrees = gd.RotationMaxAngleDegrees,
                    gs.ScanMaterialBlacklist = gd.ScanMaterialBlacklist,
                    gs.ScanMaterialWhitelist = gd.ScanMaterialWhitelist,
                    gs.ScanMaxBlocks = gd.ScanMaxBlocks,
                    gs.ScanMaxRadius = gd.ScanMaxRadius,
                    gs.ScanPlaneConstraint = gd.ScanPlaneConstraint,
                    gs.SeedBlocks = gd.SeedBlocks,
                    gs.ShowHealthDisplay = gd.ShowHealthDisplay,
                    gs.StatusDisplayMode = gd.StatusDisplayMode,
                    gs.TileEntityPolicy = gd.TileEntityPolicy;
            ");

            migrationBuilder.DropTable(
                name: "gate_doors");

            migrationBuilder.DropColumn(
                name: "AllowContinuousDamageOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "AllowPassThroughOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "CanRespawnOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "ContinuousDamageMultiplierOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "GateNameDisplayModeOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "HealthDisplayModeOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "HealthDisplayYOffsetOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "IsActiveOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "IsDestroyedOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "IsInvincibleOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "OpenedStateOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "PassThroughDurationSecondsOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "ShowHealthDisplayOverride",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "StatusDisplayModeOverride",
                table: "gate_structures");

            migrationBuilder.RenameColumn(
                name: "GateDoorId",
                table: "gate_opened_block_snapshots",
                newName: "GateStructureId");

            migrationBuilder.RenameIndex(
                name: "IX_GateOpenedBlockSnapshot_GateDoorId_SortOrder",
                table: "gate_opened_block_snapshots",
                newName: "IX_GateOpenedBlockSnapshot_GateId_SortOrder");

            migrationBuilder.RenameIndex(
                name: "IX_GateOpenedBlockSnapshot_GateDoorId",
                table: "gate_opened_block_snapshots",
                newName: "IX_GateOpenedBlockSnapshot_GateStructureId");

            migrationBuilder.RenameColumn(
                name: "GateDoorId",
                table: "gate_block_snapshots",
                newName: "GateStructureId");

            migrationBuilder.RenameIndex(
                name: "IX_GateBlockSnapshot_GateDoorId_SortOrder",
                table: "gate_block_snapshots",
                newName: "IX_GateBlockSnapshot_GateId_SortOrder");

            migrationBuilder.RenameIndex(
                name: "IX_GateBlockSnapshot_GateDoorId",
                table: "gate_block_snapshots",
                newName: "IX_GateBlockSnapshot_GateStructureId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_structures_AnchorPointId",
                table: "gate_structures",
                column: "AnchorPointId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_structures_FallbackMaterialRefId",
                table: "gate_structures",
                column: "FallbackMaterialRefId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_structures_HingeAxisId",
                table: "gate_structures",
                column: "HingeAxisId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_structures_InfoDisplayLocationId",
                table: "gate_structures",
                column: "InfoDisplayLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_structures_LeftDoorSeedBlockId",
                table: "gate_structures",
                column: "LeftDoorSeedBlockId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_structures_OpenAnchorPointId",
                table: "gate_structures",
                column: "OpenAnchorPointId");

            migrationBuilder.CreateIndex(
                name: "IX_gate_structures_ReferencePoint1Id",
                table: "gate_structures",
                column: "ReferencePoint1Id");

            migrationBuilder.CreateIndex(
                name: "IX_gate_structures_ReferencePoint2Id",
                table: "gate_structures",
                column: "ReferencePoint2Id");

            migrationBuilder.CreateIndex(
                name: "IX_gate_structures_RightDoorSeedBlockId",
                table: "gate_structures",
                column: "RightDoorSeedBlockId");

            migrationBuilder.CreateIndex(
                name: "IX_GateStructure_GateType",
                table: "gate_structures",
                column: "GateType");

            migrationBuilder.CreateIndex(
                name: "IX_GateStructure_IsActive",
                table: "gate_structures",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_GateStructure_IsOpened",
                table: "gate_structures",
                column: "IsOpened");

            migrationBuilder.AddForeignKey(
                name: "FK_gate_block_snapshots_gate_structures_GateStructureId",
                table: "gate_block_snapshots",
                column: "GateStructureId",
                principalTable: "gate_structures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_gate_opened_block_snapshots_gate_structures_GateStructureId",
                table: "gate_opened_block_snapshots",
                column: "GateStructureId",
                principalTable: "gate_structures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_gate_structures_locations_AnchorPointId",
                table: "gate_structures",
                column: "AnchorPointId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_gate_structures_locations_HingeAxisId",
                table: "gate_structures",
                column: "HingeAxisId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_gate_structures_locations_InfoDisplayLocationId",
                table: "gate_structures",
                column: "InfoDisplayLocationId",
                principalTable: "locations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_gate_structures_locations_LeftDoorSeedBlockId",
                table: "gate_structures",
                column: "LeftDoorSeedBlockId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_gate_structures_locations_OpenAnchorPointId",
                table: "gate_structures",
                column: "OpenAnchorPointId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_gate_structures_locations_ReferencePoint1Id",
                table: "gate_structures",
                column: "ReferencePoint1Id",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_gate_structures_locations_ReferencePoint2Id",
                table: "gate_structures",
                column: "ReferencePoint2Id",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_gate_structures_locations_RightDoorSeedBlockId",
                table: "gate_structures",
                column: "RightDoorSeedBlockId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_gate_structures_minecraftmaterialrefs_FallbackMaterialRefId",
                table: "gate_structures",
                column: "FallbackMaterialRefId",
                principalTable: "minecraftmaterialrefs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
