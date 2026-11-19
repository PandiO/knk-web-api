using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Item identity (PandiO/knk-workspace#141): one additive migration. The new column starts NULL on every row, so no
/// existing data can break the unique index (MySQL allows many NULLs in it); no row is written or changed.
/// </summary>
public class AddItemIdentityTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddItemIdentity().UpOperations;

    [Fact]
    public void Up_AddsANullableMatchColumn_WithAUniqueIndex_AndARestrictForeignKey()
    {
        var column = Up().OfType<AddColumnOperation>().Single();
        Assert.Equal(("item_blueprints", "VanillaMaterialMatchRefId", true), (column.Table, column.Name, column.IsNullable));

        var index = Up().OfType<CreateIndexOperation>().Single(i => i.Table == "item_blueprints");
        Assert.True(index.IsUnique);
        Assert.Equal(new[] { "VanillaMaterialMatchRefId" }, index.Columns);

        var fk = Up().OfType<AddForeignKeyOperation>().Single();
        Assert.Equal(("minecraftmaterialrefs", Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict), (fk.PrincipalTable, fk.OnDelete));
    }

    [Fact]
    public void Up_CreatesTheCountTable_UniquePerDaySourceOutcomeAndMaterial()
    {
        Assert.Equal("item_conversion_stats", Up().OfType<CreateTableOperation>().Single().Name);
        var index = Up().OfType<CreateIndexOperation>().Single(i => i.Table == "item_conversion_stats");
        Assert.True(index.IsUnique);
        Assert.Equal(new[] { "Day", "Source", "Outcome", "MaterialKey" }, index.Columns);
    }

    [Fact]
    public void Up_IsAdditiveOnly_NoDataWritten()
    {
        Assert.DoesNotContain(Up(), o => o is DropColumnOperation or DropTableOperation or AlterColumnOperation
            or InsertDataOperation or UpdateDataOperation or DeleteDataOperation or SqlOperation);
    }
}
