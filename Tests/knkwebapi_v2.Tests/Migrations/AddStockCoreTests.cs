using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Stock core P1 (PandiO/knk-workspace#97): one additive migration. Six new tables, <c>ItemBlueprint.StorageWeight</c>
/// defaulting to 1 on existing rows, every stock FK Restrict, the unique keys of the ledger, and the five system
/// storages as its only data. Nothing existing is altered or dropped.
/// </summary>
public class AddStockCoreTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddStockCore().UpOperations;

    [Fact]
    public void Up_CreatesTheSixStockTables()
    {
        Assert.Equal(new[] { "stock_lines", "stock_move_rollups", "stock_moves", "stock_transactions", "stock_units", "storages" },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n));
    }

    [Fact]
    public void Up_StorageWeight_IsOneOnExistingBlueprints()
    {
        var column = Up().OfType<AddColumnOperation>().Single();
        Assert.Equal(("item_blueprints", "StorageWeight", 1m), (column.Table, column.Name, column.DefaultValue));
        Assert.False(column.IsNullable);
    }

    [Fact]
    public void Up_EveryForeignKey_IsRestrict()
    {
        var fks = Up().OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys).ToList();
        Assert.True(fks.Count >= 15);
        Assert.All(fks, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
    }

    [Fact]
    public void Up_UniqueKeys_IdempotencyLineUnitAndRollup()
    {
        var unique = Up().OfType<CreateIndexOperation>().Where(i => i.IsUnique)
            .Select(i => $"{i.Table}:{string.Join(",", i.Columns)}").ToHashSet();
        Assert.Contains("stock_transactions:IdempotencyKey", unique);
        Assert.Contains("stock_transactions:ReversesTransactionId", unique);
        Assert.Contains("stock_lines:StorageId,ItemBlueprintId", unique);
        Assert.Contains("stock_units:ItemInstanceId", unique);
        Assert.Contains("storages:SystemKey", unique);
        Assert.Contains("stock_move_rollups:Day,Reason,FromStorageId,ToStorageId,ItemBlueprintId", unique);
    }

    [Fact]
    public void Up_HolderAndMoveCheckConstraints()
    {
        var checks = Up().OfType<CreateTableOperation>().SelectMany(t => t.CheckConstraints).Select(c => c.Name).ToHashSet();
        Assert.Contains("CK_storages_Holder", checks);
        Assert.Contains("CK_stock_moves_Item", checks);
        Assert.Contains("CK_stock_moves_Storages", checks);
    }

    [Fact]
    public void Up_IsAdditive_AndSeedsOnlyTheFiveSystemStorages()
    {
        Assert.Empty(Up().OfType<DropTableOperation>());
        Assert.Empty(Up().OfType<DropColumnOperation>());
        Assert.Empty(Up().OfType<AlterColumnOperation>());
        var insert = Assert.Single(Up().OfType<InsertDataOperation>());
        Assert.Equal("storages", insert.Table);
        Assert.Equal(5, insert.Values.GetLength(0));
    }
}
