using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-200 phase 5: the constraints migration first checks the block refs (and stops with a reason when
/// they aren't clean), then makes StateKey unique and MaterialRefId required. It deletes nothing.
/// </summary>
public class MinecraftCatalogConstraintsTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new MinecraftCatalogConstraints().UpOperations;

    [Fact]
    public void Up_ChecksTheDataBeforeAnySchemaChange()
    {
        var operations = Up();
        var firstSchemaChange = operations.ToList().FindIndex(op => op is not SqlOperation);
        var checks = operations.Take(firstSchemaChange).OfType<SqlOperation>().Select(op => op.Sql).ToList();

        Assert.Contains(checks, sql => sql.Contains("SIGNAL SQLSTATE") && sql.Contains("MaterialRefId IS NULL") && sql.Contains("HAVING COUNT(*) > 1"));
        Assert.Contains(checks, sql => sql.Contains("CALL kng200_check_block_refs"));
        Assert.DoesNotContain(operations.OfType<SqlOperation>(), op => op.Sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Up_MakesStateKeyUnique_AndMaterialRefIdRequired()
    {
        var unique = Assert.Single(Up().OfType<CreateIndexOperation>());
        Assert.Equal(("minecraftblockrefs", true), (unique.Table, unique.IsUnique));
        Assert.Equal(new[] { "StateKey" }, unique.Columns);

        var column = Assert.Single(Up().OfType<AlterColumnOperation>());
        Assert.Equal(("MaterialRefId", false), (column.Name, column.IsNullable));
    }
}
