using knkwebapi_v2.Migrations;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Shops P10 (PandiO/knk-workspace#248): one additive migration. Seven new tables (shops, shop types with their accepted
/// categories and levels, catalogue entries, hourly trade stats, town accounts) and one nullable column,
/// <c>currency_entries.TownId</c>; the currency entries' account check is widened to allow town legs (every existing row
/// still passes). Nothing else is dropped or altered and no SQL runs.
/// </summary>
public class AddShopsTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddShops().UpOperations;

    [Fact]
    public void Up_CreatesSevenTables_AddsTownIdToCurrencyEntries_AndOnlyWidensTheAccountCheck()
    {
        Assert.Equal(new[] { "shop_catalogue_entries", "shop_trade_stats", "shop_type_categories", "shop_type_levels", "shop_types", "shops", "town_accounts" },
            Up().OfType<CreateTableOperation>().Select(t => t.Name).OrderBy(n => n).ToArray());
        var column = Assert.Single(Up().OfType<AddColumnOperation>());
        Assert.Equal(("currency_entries", "TownId", true), (column.Table, column.Name, column.IsNullable));
        var dropped = Assert.Single(Up().OfType<DropCheckConstraintOperation>());
        Assert.Equal(("currency_entries", "CK_currency_entries_Account"), (dropped.Table, dropped.Name));
        var added = Up().OfType<AddCheckConstraintOperation>().Single(c => c.Table == "currency_entries");
        Assert.Contains("`AccountKind` = 0 AND `UserId` IS NOT NULL", added.Sql);
        Assert.Contains("`AccountKind` = 1 AND `UserId` IS NULL AND `SystemAccount` IS NOT NULL", added.Sql);
        Assert.Contains("`AccountKind` = 2 AND `UserId` IS NULL AND `SystemAccount` IS NULL AND `TownId` IS NOT NULL", added.Sql);
        Assert.Empty(Up().OfType<DropTableOperation>());
        Assert.Empty(Up().OfType<DropColumnOperation>());
        Assert.Empty(Up().OfType<AlterColumnOperation>());
        Assert.Empty(Up().OfType<SqlOperation>());
    }

    [Fact]
    public void AShopIsAProductionStructure_OneAccountPerTown_OneStatRowPerShopBlueprintAndHour()
    {
        var tables = Up().OfType<CreateTableOperation>().ToDictionary(t => t.Name);
        var shopKeys = tables["shops"].ForeignKeys.ToDictionary(fk => fk.PrincipalTable, fk => fk.OnDelete);
        Assert.Equal(ReferentialAction.Cascade, shopKeys["production_structures"]); // TPT: the row goes with its structure
        Assert.Equal(ReferentialAction.Restrict, shopKeys["shop_types"]);
        Assert.Equal(ReferentialAction.SetNull, shopKeys["locations"]);
        var indexes = Up().OfType<CreateIndexOperation>().ToList();
        Assert.True(Assert.Single(indexes, i => i.Table == "town_accounts" && i.Columns.SequenceEqual(new[] { "TownId" })).IsUnique);
        Assert.True(Assert.Single(indexes, i => i.Table == "shop_trade_stats" && i.Columns.SequenceEqual(new[] { "ShopId", "ItemBlueprintId", "HourStart" })).IsUnique);
        Assert.True(Assert.Single(indexes, i => i.Table == "shop_type_levels" && i.Columns.SequenceEqual(new[] { "ShopTypeId", "Level" })).IsUnique);
        var entry = tables["shop_catalogue_entries"].ForeignKeys.ToDictionary(fk => fk.PrincipalTable, fk => fk.OnDelete);
        Assert.Equal((ReferentialAction.Cascade, ReferentialAction.Restrict, ReferentialAction.Restrict),
            (entry["shops"], entry["item_blueprints"], entry["item_instances"]));
        Assert.Contains(tables["town_accounts"].CheckConstraints, c => c.Sql == "`Coins` >= 0");
    }

    [Fact]
    public void TheShopSettings_HaveTheirDefaults_AndANullSectionKeepsTheStoredOne()
    {
        var d = ProductionSettings.Read(null).Shops!;
        Assert.Equal((10m, 2), (d.EnchantmentPercentPerLevel, d.StatsLookbackHours));
        var stored = new knkwebapi_v2.Dtos.ProductionSettingsDto { Shops = new knkwebapi_v2.Dtos.ProductionShopSettingsDto { EnchantmentPercentPerLevel = 7 } };
        Assert.Equal(7, ProductionSettings.Normalize(new knkwebapi_v2.Dtos.ProductionSettingsDto(), stored).Shops!.EnchantmentPercentPerLevel);
    }
}
