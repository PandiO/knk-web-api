using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Production world P2 (PandiO/knk-workspace#91): one additive migration. <c>structures.DistrictId</c> and
/// <c>StreetId</c> become nullable (existing rows keep their values), a nullable <c>TownId</c>, the District-xor-Town check
/// (every existing structure has a district, so it holds), and the two subtype tables. No column or table is dropped, no
/// data is changed; the structure FKs are re-created without ON DELETE (the database refuses the delete as before).
/// </summary>
public class AddProductionWorldTests
{
    private static IReadOnlyList<MigrationOperation> Up() => new AddProductionWorld().UpOperations;

    [Fact]
    public void Up_CreatesTheTwoSubtypeTables_KeyedOnTheStructure()
    {
        var tables = Up().OfType<CreateTableOperation>().ToList();
        Assert.Equal(new[] { "production_structures", "warehouses" }, tables.Select(t => t.Name).OrderBy(n => n));
        Assert.All(tables, t => Assert.Contains(t.ForeignKeys, fk => fk.PrincipalTable == "structures" && fk.OnDelete == ReferentialAction.Cascade));
    }

    [Fact]
    public void Up_OnlyWidensTheStructureColumns_AndAddsANullableTownId()
    {
        var altered = Up().OfType<AlterColumnOperation>().ToList();
        Assert.Equal(new[] { "DistrictId", "StreetId" }, altered.Select(a => a.Name).OrderBy(n => n));
        Assert.All(altered, a => Assert.True(a.IsNullable && !a.OldColumn.IsNullable));
        var added = Assert.Single(Up().OfType<AddColumnOperation>());
        Assert.Equal(("structures", "TownId", true), (added.Table, added.Name, added.IsNullable));
    }

    [Fact]
    public void Up_AddsTheDistrictXorTownCheck_AndDropsNothing()
    {
        var check = Assert.Single(Up().OfType<AddCheckConstraintOperation>());
        Assert.Equal(("structures", "CK_structures_DistrictXorTown"), (check.Table, check.Name));
        Assert.Empty(Up().OfType<DropTableOperation>());
        Assert.Empty(Up().OfType<DropColumnOperation>());
        Assert.Empty(Up().OfType<SqlOperation>());
    }

    [Fact]
    public void Up_StructureForeignKeys_HaveNoDeleteAction()
    {
        var fks = Up().OfType<AddForeignKeyOperation>().Where(f => f.Table == "structures").ToList();
        Assert.Equal(new[] { "DistrictId", "StreetId", "TownId" }, fks.Select(f => f.Columns.Single()).OrderBy(n => n));
        Assert.All(fks, fk => Assert.Equal(ReferentialAction.NoAction, fk.OnDelete));
    }
}
