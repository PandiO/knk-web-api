using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Item delivery P7 (PandiO/knk-workspace#60): one additive migration. Two new tables (deliveries and their inbox
/// lots); nothing is dropped, altered or added to existing tables and no SQL runs. A key delivers once, lines go with
/// their delivery, and a delivery never takes a user, storage, blueprint or item instance with it.
/// </summary>
public class AddItemDeliveryTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddItemDelivery().UpOperations;

    [Fact]
    public void Up_CreatesTheTwoTables_AndChangesNothingElse()
    {
        Assert.Equal(new[] { "item_deliveries", "item_delivery_lines" },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n).ToArray());
        Assert.Empty(Up().OfType<AddColumnOperation>());
        Assert.Empty(Up().OfType<DropTableOperation>());
        Assert.Empty(Up().OfType<DropColumnOperation>());
        Assert.Empty(Up().OfType<AlterColumnOperation>());
        Assert.Empty(Up().OfType<SqlOperation>());
    }

    [Fact]
    public void AKeyDeliversOnce_LinesGoWithTheirDelivery_AndNothingElseCascades()
    {
        var key = Assert.Single(Up().OfType<CreateIndexOperation>(), i => i.Table == "item_deliveries" && i.Columns.SequenceEqual(new[] { "IdempotencyKey" }));
        Assert.True(key.IsUnique);
        var tables = Up().OfType<CreateTableOperation>().ToDictionary(t => t.Name);
        Assert.All(tables["item_deliveries"].ForeignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        var lines = tables["item_delivery_lines"].ForeignKeys.ToDictionary(fk => fk.PrincipalTable, fk => fk.OnDelete);
        Assert.Equal(ReferentialAction.Cascade, lines["item_deliveries"]);
        Assert.All(lines.Where(kv => kv.Key != "item_deliveries"), kv => Assert.Equal(ReferentialAction.Restrict, kv.Value));
    }
}
