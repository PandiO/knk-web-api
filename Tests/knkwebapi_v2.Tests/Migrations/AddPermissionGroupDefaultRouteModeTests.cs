using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// PandiO/knk-workspace#852: the migration is additive, one nullable column on <c>permission_groups</c> and nothing
/// else, so every existing group keeps no default (null) and routes stay as today.
/// </summary>
public class AddPermissionGroupDefaultRouteModeTests
{
    [Fact]
    public void Up_OnlyAddsTheNullableColumn()
    {
        var add = Assert.IsType<AddColumnOperation>(Assert.Single(new AddPermissionGroupDefaultRouteMode().UpOperations));
        Assert.Equal("permission_groups", add.Table);
        Assert.Equal("DefaultRouteMode", add.Name);
        Assert.Equal(typeof(string), add.ClrType);
        Assert.True(add.IsNullable);
        Assert.Equal(16, add.MaxLength);
        Assert.Null(add.DefaultValue);
        Assert.Null(add.DefaultValueSql);
    }

    [Fact]
    public void Down_DropsOnlyTheColumn()
    {
        var drop = Assert.IsType<DropColumnOperation>(Assert.Single(new AddPermissionGroupDefaultRouteMode().DownOperations));
        Assert.Equal("permission_groups", drop.Table);
        Assert.Equal("DefaultRouteMode", drop.Name);
    }
}
