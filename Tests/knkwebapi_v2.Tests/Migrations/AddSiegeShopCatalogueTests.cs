using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Siege shop L2 (PandiO/knk-workspace#867, charter § 3.4): the migration is additive. Three new tables with the unique
/// category key and the unique (scenario, entry) override, the three seeded categories, one nullable column on
/// item_blueprints, and nothing dropped, renamed or altered.
/// </summary>
public class AddSiegeShopCatalogueTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddSiegeShopCatalogue().UpOperations;

    [Fact]
    public void Up_IsAdditiveOnly()
    {
        Assert.All(Up(), op => Assert.True(
            op is CreateTableOperation or CreateIndexOperation or AddColumnOperation or InsertDataOperation, op.GetType().Name));
        Assert.Equal(
            new[] { "siege_scenario_shop_overrides", "siege_shop_categories", "siege_shop_entries" },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Up_PotionType_IsANullableColumnOnItemBlueprints()
    {
        var column = Assert.Single(Up().OfType<AddColumnOperation>());
        Assert.Equal(("item_blueprints", "PotionType"), (column.Table, column.Name));
        Assert.True(column.IsNullable);
    }

    [Fact]
    public void Up_SeedsTheThreeCategories()
    {
        var insert = Assert.Single(Up().OfType<InsertDataOperation>());
        Assert.Equal("siege_shop_categories", insert.Table);
        var keyColumn = Array.IndexOf(insert.Columns, "Key");
        var keys = Enumerable.Range(0, insert.Values.GetLength(0)).Select(r => (string)insert.Values[r, keyColumn]!).ToArray();
        Assert.Equal(new[] { "siege-weapons", "consumables", "magic" }, keys);
    }

    [Fact]
    public void Up_UniqueIndexes_KeyAndScenarioEntry()
    {
        var unique = Up().OfType<CreateIndexOperation>().Where(i => i.IsUnique).Select(i => $"{i.Table}({string.Join(",", i.Columns)})").ToList();
        Assert.Contains("siege_shop_categories(Key)", unique);
        Assert.Contains("siege_scenario_shop_overrides(SiegeScenarioId,SiegeShopEntryId)", unique);
    }

    [Fact]
    public void Up_Entries_KeepEveryBrakeOptional_AndStoreEnumsAsText()
    {
        var entries = Up().OfType<CreateTableOperation>().Single(t => t.Name == "siege_shop_entries").Columns.ToDictionary(c => c.Name);
        foreach (var name in new[] { "SiegeScenarioId", "ItemBlueprintId", "SiegeWeaponTemplateId", "Price", "PriceStep",
                     "PriceGrowthFactor", "PriceMax", "CooldownSeconds", "CooldownStepSeconds", "CooldownGrowthFactor",
                     "CooldownMaxSeconds", "MaxPerMatch", "DisplayName", "IconMaterial" })
            Assert.True(entries[name].IsNullable, name);
        foreach (var name in new[] { "GameMode", "Kind", "PriceCurve", "CooldownCurve" })
            Assert.Equal(typeof(string), entries[name].ClrType);
        Assert.False(entries["Quantity"].IsNullable);
        Assert.False(entries["Enabled"].IsNullable);
    }
}
