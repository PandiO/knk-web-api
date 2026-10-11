using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using knkwebapi_v2.Properties;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// KNG-132: the GateDoor form's anchor, reference point, geometry and motion distance fields had no description,
    /// and a Reference point 2 placed above Reference point 1 (instead of above the anchor) gave a sheared door that
    /// jammed. Fills in what each one means (gate spec, docs/specs/gate-structure-animation/SPEC.md), only where a
    /// GateDoor form's field still has no description, so text an admin wrote is kept. Data only, no schema change.
    /// Seeding default forms and field descriptions in general is a separate proposal; this covers these fields.
    /// </summary>
    [DbContext(typeof(KnKDbContext))]
    [Migration("20261011193000_DescribeGateDoorGeometryFields")]
    public partial class DescribeGateDoorGeometryFields : Migration
    {
        public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            ["AnchorPointId"] = "The bottom corner of the door, where its block grid starts. "
                + "Reference point 1 and 2 are both measured from this point.",
            ["ReferencePoint1Id"] = "The other end of the door's bottom edge, on the same row as the anchor. "
                + "Anchor to this point is the door's width.",
            ["ReferencePoint2Id"] = "The top of the door straight above the anchor (not above reference point 1). "
                + "Anchor to this point is the door's height and must be at right angles to the width.",
            ["GeometryWidth"] = "Blocks along the bottom edge, anchor to reference point 1, both included. "
                + "Filled in from the points.",
            ["GeometryHeight"] = "Blocks from the anchor up to reference point 2, both included. "
                + "Filled in from the points.",
            ["GeometryDepth"] = "How many blocks thick the door is, through the wall, counted from the anchor's layer.",
            ["MotionDistanceBlocks"] = "How many blocks the door moves when it opens. "
                + "0 uses its height for a vertical door and its width for a lateral one.",
        };

        private static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

        private static string GateDoorField(string fieldName) =>
            "`f`.`FieldName` = " + Quote(fieldName)
            + " AND `f`.`FormStepId` IN (SELECT `s`.`Id` FROM `FormSteps` `s` JOIN `FormConfigurations` `c`"
            + " ON `c`.`Id` = `s`.`FormConfigurationId` WHERE `c`.`EntityTypeName` = 'GateDoor')";

        public static IEnumerable<string> UpSql() => Descriptions.Select(d =>
            "UPDATE `FormFields` `f` SET `f`.`Description` = " + Quote(d.Value)
            + " WHERE " + GateDoorField(d.Key) + " AND (`f`.`Description` IS NULL OR `f`.`Description` = '');");

        public static IEnumerable<string> DownSql() => Descriptions.Select(d =>
            "UPDATE `FormFields` `f` SET `f`.`Description` = NULL"
            + " WHERE " + GateDoorField(d.Key) + " AND `f`.`Description` = " + Quote(d.Value) + ";");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in UpSql()) migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the text this migration wrote; a description edited since stays.
            foreach (var sql in DownSql()) migrationBuilder.Sql(sql);
        }
    }
}
