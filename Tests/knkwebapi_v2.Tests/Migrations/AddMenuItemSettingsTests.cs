using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Siege shop L5 (PandiO/knk-workspace#870, charter § 3.4): the migration is additive, one nullable JSON column on
/// game_settings. An existing row keeps null there and reads the seeded defaults (<c>MenuItemSettingsTests</c>).
/// </summary>
public class AddMenuItemSettingsTests
{
    [Fact]
    public void Up_AddsOneNullableJsonColumnOnGameSettings_AndNothingElse()
    {
        var ops = new AddMenuItemSettings().UpOperations;

        var column = Assert.IsType<AddColumnOperation>(Assert.Single(ops));
        Assert.Equal(("game_settings", "MenuItemSettingsJson"), (column.Table, column.Name));
        Assert.True(column.IsNullable);
        Assert.Equal("longtext", column.ColumnType);
    }

    [Fact]
    public void Down_DropsOnlyThatColumn()
    {
        var drop = Assert.IsType<DropColumnOperation>(Assert.Single(new AddMenuItemSettings().DownOperations));
        Assert.Equal(("game_settings", "MenuItemSettingsJson"), (drop.Table, drop.Name));
    }
}
