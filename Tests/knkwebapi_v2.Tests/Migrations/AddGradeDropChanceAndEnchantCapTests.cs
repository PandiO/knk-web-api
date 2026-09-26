using FluentAssertions;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Linear KNG-6: the migration adds both columns and backfills grades 1-5 in <c>Up</c> (the create-only seeds
/// never touch existing rows). The SQL itself was also run against MySQL 8 when the migration was written;
/// the Migrations (fresh DB) workflow re-applies it on every push.
/// </summary>
public class AddGradeDropChanceAndEnchantCapTests
{
    private static IReadOnlyList<MigrationOperation> UpOperations()
    {
        var migration = new AddGradeDropChanceAndEnchantCap();
        return migration.UpOperations;
    }

    [Fact]
    public void Up_AddsBothNullableColumnsToGrades()
    {
        var columns = UpOperations().OfType<AddColumnOperation>().ToList();

        columns.Select(c => (c.Table, c.Name, c.IsNullable)).Should().BeEquivalentTo(new[]
        {
            ("grades", "DropChance", true),
            ("grades", "EnchantLevelCapDivisor", true),
        });
        columns.Single(c => c.Name == "DropChance").ColumnType.Should().Be("decimal(7,4)");
    }

    [Fact]
    public void Up_BackfillsGradesOneToFiveByStars_AfterAddingTheColumns()
    {
        var ops = UpOperations();
        var sql = ops.OfType<SqlOperation>().Select(o => o.Sql).ToList();

        sql.Should().Equal(
            "UPDATE `grades` SET `DropChance` = 70, `EnchantLevelCapDivisor` = 5 WHERE `Stars` = 1;",
            "UPDATE `grades` SET `DropChance` = 60, `EnchantLevelCapDivisor` = 4 WHERE `Stars` = 2;",
            "UPDATE `grades` SET `DropChance` = 40, `EnchantLevelCapDivisor` = 3 WHERE `Stars` = 3;",
            "UPDATE `grades` SET `DropChance` = 25, `EnchantLevelCapDivisor` = 2 WHERE `Stars` = 4;",
            "UPDATE `grades` SET `DropChance` = 15, `EnchantLevelCapDivisor` = 1 WHERE `Stars` = 5;");
        ops.ToList().FindIndex(o => o is SqlOperation).Should().BeGreaterThan(ops.ToList().FindLastIndex(o => o is AddColumnOperation));
    }

    [Fact]
    public void Backfill_MatchesTheSeededDefaults()
    {
        var defaults = knkwebapi_v2.Models.GradeDefaults.All.Where(g => g.Stars <= 5)
            .Select(g => (g.Stars, g.DropChance, g.EnchantLevelCapDivisor!.Value));
        AddGradeDropChanceAndEnchantCap.Backfill.Should().Equal(defaults);
        knkwebapi_v2.Models.GradeDefaults.All.Where(g => g.Stars > 5).Should().OnlyContain(g => g.EnchantLevelCapDivisor == null);
    }

    [Fact]
    public void Down_DropsBothColumns()
    {
        var migration = new AddGradeDropChanceAndEnchantCap();
        migration.DownOperations.OfType<DropColumnOperation>().Select(o => o.Name)
            .Should().BeEquivalentTo("DropChance", "EnchantLevelCapDivisor");
    }
}
