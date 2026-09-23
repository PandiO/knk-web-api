using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryMenuTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "menu_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Key = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Growth = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BackgroundMaterialRefId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_templates_minecraftmaterialrefs_BackgroundMaterialRefId",
                        column: x => x.BackgroundMaterialRefId,
                        principalTable: "minecraftmaterialrefs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "menu_section_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MenuTemplateId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kind = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DisplaySlot = table.Column<int>(type: "int", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    PositionMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AlignVertical = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AlignHorizontal = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Overflow = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ListMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Priority = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VisibilityPermission = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_section_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_section_templates_menu_templates_MenuTemplateId",
                        column: x => x.MenuTemplateId,
                        principalTable: "menu_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "menu_item_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MenuSectionTemplateId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    SlotOverride = table.Column<int>(type: "int", nullable: true),
                    MaterialRefId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    ChatColorName = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChatColorDescription = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VisibilityPermission = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActionPermission = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_item_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_item_templates_menu_section_templates_MenuSectionTempla~",
                        column: x => x.MenuSectionTemplateId,
                        principalTable: "menu_section_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_menu_item_templates_minecraftmaterialrefs_MaterialRefId",
                        column: x => x.MaterialRefId,
                        principalTable: "minecraftmaterialrefs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "menu_action_bindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MenuItemTemplateId = table.Column<int>(type: "int", nullable: false),
                    ActionTypeId = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ParamsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_action_bindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_action_bindings_menu_item_templates_MenuItemTemplateId",
                        column: x => x.MenuItemTemplateId,
                        principalTable: "menu_item_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "menu_variable_bindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MenuSectionTemplateId = table.Column<int>(type: "int", nullable: true),
                    MenuItemTemplateId = table.Column<int>(type: "int", nullable: true),
                    TargetProperty = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Expression = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RefreshPolicy = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TtlTicks = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_variable_bindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_variable_bindings_menu_item_templates_MenuItemTemplateId",
                        column: x => x.MenuItemTemplateId,
                        principalTable: "menu_item_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_menu_variable_bindings_menu_section_templates_MenuSectionTem~",
                        column: x => x.MenuSectionTemplateId,
                        principalTable: "menu_section_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "menu_condition_bindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MenuItemTemplateId = table.Column<int>(type: "int", nullable: false),
                    ActionBindingId = table.Column<int>(type: "int", nullable: true),
                    ConditionTypeId = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ParamsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_condition_bindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_condition_bindings_menu_action_bindings_ActionBindingId",
                        column: x => x.ActionBindingId,
                        principalTable: "menu_action_bindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_menu_condition_bindings_menu_item_templates_MenuItemTemplate~",
                        column: x => x.MenuItemTemplateId,
                        principalTable: "menu_item_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_ActionBinding_MenuItemTemplateId_SortOrder",
                table: "menu_action_bindings",
                columns: new[] { "MenuItemTemplateId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ConditionBinding_MenuItemTemplateId_SortOrder",
                table: "menu_condition_bindings",
                columns: new[] { "MenuItemTemplateId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_condition_bindings_ActionBindingId",
                table: "menu_condition_bindings",
                column: "ActionBindingId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_item_templates_MaterialRefId",
                table: "menu_item_templates",
                column: "MaterialRefId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemTemplate_MenuSectionTemplateId_SortOrder",
                table: "menu_item_templates",
                columns: new[] { "MenuSectionTemplateId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuSectionTemplate_MenuTemplateId_Name",
                table: "menu_section_templates",
                columns: new[] { "MenuTemplateId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuSectionTemplate_MenuTemplateId_SortOrder",
                table: "menu_section_templates",
                columns: new[] { "MenuTemplateId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_templates_BackgroundMaterialRefId",
                table: "menu_templates",
                column: "BackgroundMaterialRefId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_templates_Key",
                table: "menu_templates",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_menu_variable_bindings_MenuItemTemplateId",
                table: "menu_variable_bindings",
                column: "MenuItemTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_variable_bindings_MenuSectionTemplateId",
                table: "menu_variable_bindings",
                column: "MenuSectionTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "menu_condition_bindings");

            migrationBuilder.DropTable(
                name: "menu_variable_bindings");

            migrationBuilder.DropTable(
                name: "menu_action_bindings");

            migrationBuilder.DropTable(
                name: "menu_item_templates");

            migrationBuilder.DropTable(
                name: "menu_section_templates");

            migrationBuilder.DropTable(
                name: "menu_templates");
        }
    }
}
