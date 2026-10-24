using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// KNG-200 phase 2: the catalogue migration is additive only (plan §8 step 1). It adds columns, one
/// table, plain indexes and a Restrict FK; it never drops, alters or touches existing data.
/// </summary>
public class MinecraftCatalogEntryFieldsTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new MinecraftCatalogEntryFields().UpOperations;

    [Fact]
    public void Up_IsAdditiveOnly()
    {
        Assert.All(Up(), op => Assert.True(
            op is AddColumnOperation or CreateTableOperation or CreateIndexOperation or AddForeignKeyOperation,
            op.GetType().Name));
        Assert.Equal("minecraft_catalog_scans", Assert.Single(Up().OfType<CreateTableOperation>()).Name);
    }

    [Fact]
    public void Up_NewColumnsAreNullable_ExceptCatalogStatus_WhichDefaultsToUnverified()
    {
        var columns = Up().OfType<AddColumnOperation>().ToList();

        var status = Assert.Single(columns, c => c.Name == "CatalogStatus");
        Assert.False(status.IsNullable);
        Assert.Equal("Unverified", status.DefaultValue);
        Assert.All(columns.Where(c => c.Name != "CatalogStatus"), c => Assert.True(c.IsNullable, c.Name));
    }

    [Fact]
    public void Up_StateKeyIsNotUniqueYet_AndBlockRefsPointAtTheirMaterialWithRestrict()
    {
        var stateKeyIndex = Assert.Single(Up().OfType<CreateIndexOperation>(), i => i.Columns.SequenceEqual(new[] { "StateKey" }));
        Assert.False(stateKeyIndex.IsUnique);

        var fk = Assert.Single(Up().OfType<AddForeignKeyOperation>());
        Assert.Equal(("minecraftblockrefs", "minecraftmaterialrefs", ReferentialAction.Restrict), (fk.Table, fk.PrincipalTable, fk.OnDelete));
    }
}
