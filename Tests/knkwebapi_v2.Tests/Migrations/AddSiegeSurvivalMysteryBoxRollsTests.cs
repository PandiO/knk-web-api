using FluentAssertions;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Siege Survival phase 4 (PandiO/knk-workspace#41, link S7a): AddSiegeSurvivalMysteryBoxRolls only creates the two
/// roll tables and their indexes (one roll per roll id, one delivery per roll), touches no existing table, and its
/// Down drops exactly those tables. Generated at design time (dotnet-ef 9.0.10, MySQL 8.0.36) on top of
/// AddSiegeSurvivalPhase1.
/// </summary>
public class AddSiegeSurvivalMysteryBoxRollsTests
{
    [Fact]
    public void Up_CreatesTheRollTables_AndNothingElse()
    {
        var ops = new AddSiegeSurvivalMysteryBoxRolls().UpOperations;

        ops.OfType<CreateTableOperation>().Select(t => t.Name).Should().BeEquivalentTo("siege_survival_mystery_box_rolls", "siege_survival_mystery_box_deliveries");
        ops.Should().OnlyContain(o => o is CreateTableOperation || o is CreateIndexOperation);
        ops.OfType<CreateIndexOperation>().Select(i => i.Table).Distinct().Should().BeEquivalentTo("siege_survival_mystery_box_rolls", "siege_survival_mystery_box_deliveries");
        ops.OfType<CreateIndexOperation>().Where(i => i.IsUnique).Select(i => (i.Table, string.Join(",", i.Columns))).Should().BeEquivalentTo(new[]
        {
            ("siege_survival_mystery_box_rolls", "RollId"),
            ("siege_survival_mystery_box_deliveries", "SurvivalMysteryBoxRollId"),
        });

        var rolls = ops.OfType<CreateTableOperation>().Single(t => t.Name == "siege_survival_mystery_box_rolls");
        rolls.ForeignKeys.Select(f => (f.PrincipalTable, f.OnDelete)).Should().BeEquivalentTo(new[]
        {
            ("siege_matches", ReferentialAction.Cascade),
            ("users", ReferentialAction.Restrict),
        });
        rolls.Columns.Select(c => c.Name).Should().Contain(new[] { "RollId", "SurvivalMysteryBoxPointId", "SurvivalMysteryBoxEntryId", "EntryJson", "ReelJson", "PriceCharged", "State", "RolledAt", "ClaimedAt", "ForfeitedAt" });
        rolls.Columns.Single(c => c.Name == "RollId").MaxLength.Should().Be(64);

        var deliveries = ops.OfType<CreateTableOperation>().Single(t => t.Name == "siege_survival_mystery_box_deliveries");
        deliveries.ForeignKeys.Select(f => (f.PrincipalTable, f.OnDelete)).Should().Equal(("siege_survival_mystery_box_rolls", ReferentialAction.Cascade));
    }

    [Fact]
    public void Down_DropsExactlyTheRollTables()
    {
        var ops = new AddSiegeSurvivalMysteryBoxRolls().DownOperations;
        ops.Should().OnlyContain(o => o is DropTableOperation);
        ops.OfType<DropTableOperation>().Select(t => t.Name).Should().BeEquivalentTo("siege_survival_mystery_box_rolls", "siege_survival_mystery_box_deliveries");
    }
}
