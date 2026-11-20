using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Terminals and menus P6a (PandiO/knk-workspace#683): one additive migration. One new table; nothing is dropped,
/// altered or added to existing tables and no SQL runs. A block is bound at most once, and containers go with their
/// storage.
/// </summary>
public class AddStorageContainersTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddStorageContainers().UpOperations;

    [Fact]
    public void Up_CreatesTheContainerTable_AndChangesNothingElse()
    {
        Assert.Equal("storage_containers", Assert.Single(Up().OfType<CreateTableOperation>()).Name);
        Assert.Empty(Up().OfType<AddColumnOperation>());
        Assert.Empty(Up().OfType<DropTableOperation>());
        Assert.Empty(Up().OfType<DropColumnOperation>());
        Assert.Empty(Up().OfType<AlterColumnOperation>());
        Assert.Empty(Up().OfType<SqlOperation>());
    }

    [Fact]
    public void ABlockIsBoundOnce_AndContainersGoWithTheirStorage()
    {
        var unique = Assert.Single(Up().OfType<CreateIndexOperation>(), i => i.Columns.SequenceEqual(new[] { "WorldName", "X", "Y", "Z" }));
        Assert.True(unique.IsUnique);
        var fk = Assert.Single(Assert.Single(Up().OfType<CreateTableOperation>()).ForeignKeys);
        Assert.Equal(("storages", ReferentialAction.Cascade), (fk.PrincipalTable, fk.OnDelete));
    }
}
