using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using knkwebapi_v2.Properties;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// KNG-119: entity metadata reported the placeholder "default" as the default value of every nullable
    /// value-type property (int?, bool?, an enum? such as Domain.RoadAccessOverride), and the Form Builder
    /// copied it into FormFields.DefaultValue. The wizard then submitted "default" in place of an empty value,
    /// which the API rejects (e.g. "roadAccessOverride must be Applies, Ignored or empty"). MetadataService no
    /// longer reports it; this clears the copies already saved. String fields are left alone, since "default"
    /// can be a typed text there; the bug only produced it for non-String types. Data only, no schema change.
    /// </summary>
    [DbContext(typeof(KnKDbContext))]
    [Migration("20261010120000_ClearPlaceholderFormFieldDefaults")]
    public partial class ClearPlaceholderFormFieldDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FieldType 0 = String (Enums/FieldType.cs).
            migrationBuilder.Sql("UPDATE `FormFields` SET `DefaultValue` = NULL WHERE `DefaultValue` = 'default' AND `FieldType` <> 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The cleared placeholder was never a valid value; nothing to restore.
        }
    }
}
