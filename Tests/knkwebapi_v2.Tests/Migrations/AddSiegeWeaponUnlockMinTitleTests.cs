using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Siege shop L4 (PandiO/knk-workspace#869, charter § 3.4): additive, one nullable column on siege_weapon_templates
/// with its foreign key to title_brackets (Restrict). Existing templates keep null: no minimum title.
/// </summary>
public class AddSiegeWeaponUnlockMinTitleTests
{
    [Fact]
    public void Up_AddsTheNullableColumn_ItsIndex_AndARestrictForeignKey()
    {
        var ops = new AddSiegeWeaponUnlockMinTitle().UpOperations;
        Assert.Equal(3, ops.Count);

        var column = Assert.Single(ops.OfType<AddColumnOperation>());
        Assert.Equal(("siege_weapon_templates", "UnlockMinTitleLevel"), (column.Table, column.Name));
        Assert.True(column.IsNullable);
        Assert.Null(column.DefaultValue);

        Assert.Single(ops.OfType<CreateIndexOperation>());
        var fk = Assert.Single(ops.OfType<AddForeignKeyOperation>());
        Assert.Equal(("title_brackets", ReferentialAction.Restrict), (fk.PrincipalTable, fk.OnDelete));
    }

    [Fact]
    public void Down_RemovesOnlyWhatUpAdded()
    {
        var ops = new AddSiegeWeaponUnlockMinTitle().DownOperations;
        Assert.Equal(3, ops.Count);
        Assert.Equal("UnlockMinTitleLevel", Assert.Single(ops.OfType<DropColumnOperation>()).Name);
    }
}
