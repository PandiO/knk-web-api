using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Transport core P5 (PandiO/knk-workspace#79): one additive migration. Seven new tables; nothing is dropped, altered or
/// added to existing tables and no SQL runs. Each order owns one transit storage (unique), a client key makes one order,
/// lanes are unique per direction, and the sweep finds due orders by an index.
/// </summary>
public class AddTransportCoreTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddTransportCore().UpOperations;

    [Fact]
    public void Up_CreatesTheTransportTables_AndChangesNothingElse()
    {
        Assert.Equal(new[]
        {
            "transport_assignments", "transport_events", "transport_lanes", "transport_order_lines", "transport_orders",
            "transport_stop_lines", "transport_stops",
        }, Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Empty(Up().OfType<AddColumnOperation>());
        Assert.Empty(Up().OfType<DropTableOperation>());
        Assert.Empty(Up().OfType<DropColumnOperation>());
        Assert.Empty(Up().OfType<AlterColumnOperation>());
        Assert.Empty(Up().OfType<SqlOperation>());
    }

    [Fact]
    public void TheIndexes_KeepOneTransitStoragePerOrder_OneOrderPerClientKey_OneLanePerDirection_AndFindDueOrders()
    {
        bool Unique(string table, params string[] cols) =>
            Assert.Single(Up().OfType<CreateIndexOperation>(), i => i.Table == table && i.Columns.SequenceEqual(cols)).IsUnique;
        Assert.True(Unique("transport_orders", "CargoStorageId"));
        Assert.True(Unique("transport_orders", "ClientKey"));
        Assert.True(Unique("transport_lanes", "FromStructureId", "ToStructureId"));
        Assert.True(Unique("transport_stop_lines", "TransportStopId", "ItemBlueprintId"));
        Assert.True(Unique("transport_order_lines", "TransportOrderId", "ItemBlueprintId"));
        Assert.False(Unique("transport_orders", "NextActionAt"));
    }

    [Fact]
    public void StoragesAndBlueprints_AnOrderNames_AreRestrict_AndChildrenGoWithTheirOrder()
    {
        var orders = Up().OfType<CreateTableOperation>().Single(t => t.Name == "transport_orders");
        Assert.All(orders.ForeignKeys.Where(f => f.PrincipalTable == "storages"),
            f => Assert.Equal(Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict, f.OnDelete));
        foreach (var child in new[] { "transport_stops", "transport_order_lines", "transport_assignments", "transport_events" })
        {
            var fk = Up().OfType<CreateTableOperation>().Single(t => t.Name == child).ForeignKeys.Single(f => f.PrincipalTable == "transport_orders");
            Assert.Equal(Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Cascade, fk.OnDelete);
        }
    }
}
