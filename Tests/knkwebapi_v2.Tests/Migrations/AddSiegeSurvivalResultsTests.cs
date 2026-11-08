using FluentAssertions;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// Siege Survival phase 5 (PandiO/knk-workspace#41, link S8a): AddSiegeSurvivalResults only creates the result,
/// player result and checkpoint tables with their indexes (one result and one checkpoint per match, one player row
/// per result and user), touches no existing table, and its Down drops exactly those tables. Generated at design time
/// (dotnet-ef 9.0.10, MySQL 8.0.36) on top of AddSiegeSurvivalMysteryBoxRolls.
/// </summary>
public class AddSiegeSurvivalResultsTests
{
    private const string Results = "siege_survival_match_results";
    private const string Players = "siege_survival_match_player_results";
    private const string Checkpoints = "siege_survival_match_checkpoints";

    [Fact]
    public void Up_CreatesTheThreeTables_AndNothingElse()
    {
        var ops = new AddSiegeSurvivalResults().UpOperations;

        ops.OfType<CreateTableOperation>().Select(t => t.Name).Should().BeEquivalentTo(Results, Players, Checkpoints);
        ops.Should().OnlyContain(o => o is CreateTableOperation || o is CreateIndexOperation);
        ops.OfType<CreateIndexOperation>().Select(i => i.Table).Distinct().Should().BeEquivalentTo(Results, Players, Checkpoints);
        ops.OfType<CreateIndexOperation>().Where(i => i.IsUnique).Select(i => (i.Table, string.Join(",", i.Columns))).Should().BeEquivalentTo(new[]
        {
            (Results, "SiegeMatchId"),
            (Players, "SurvivalMatchResultId,UserId"),
            (Checkpoints, "SiegeMatchId"),
        });

        var results = ops.OfType<CreateTableOperation>().Single(t => t.Name == Results);
        results.ForeignKeys.Select(f => (f.PrincipalTable, f.OnDelete)).Should().Equal(("siege_matches", ReferentialAction.Cascade));
        results.Columns.Select(c => c.Name).Should().Contain(new[]
        {
            "SiegeMatchId", "SiegeLobbyId", "SiegeScenarioId", "DifficultyProfileId", "EquipmentMode", "EndReason", "ReachedWave", "CompletedWave",
            "StartedAt", "EndedAt", "PlayersAtStart", "PlayersAtEnd", "EarnedKills", "SystemKills", "RewardPolicyVersion", "SettlesRewards",
            "TotalCoinsSettled", "TotalXpSettled", "LedgerKey", "LedgerPostingPublicId", "SettledAt", "Source", "RequestFingerprint", "RecordedAt"
        });
        // Plain ids: a deleted difficulty profile never takes match history with it.
        results.ForeignKeys.Should().HaveCount(1);

        var players = ops.OfType<CreateTableOperation>().Single(t => t.Name == Players);
        players.ForeignKeys.Select(f => (f.PrincipalTable, f.OnDelete)).Should().BeEquivalentTo(new[]
        {
            (Results, ReferentialAction.Cascade),
            ("users", ReferentialAction.Restrict),
        });
        players.Columns.Select(c => c.Name).Should().Contain(new[]
        {
            "UserId", "PresentAtEnd", "LeftEarly", "LeftAt", "KitId", "Kills", "BonusKills", "Deaths", "RepairWork", "MatchCurrencyEarned",
            "MatchCurrencySpent", "FinalMatchBalance", "CoinsAccrued", "XpAccrued", "CoinsSettled", "XpSettled", "MysteryBoxRolls", "MysteryBoxClaims", "MysteryBoxForfeits"
        });
        players.Columns.Single(c => c.Name == "CoinsAccrued").ColumnType.Should().Be("decimal(14,4)");

        var checkpoints = ops.OfType<CreateTableOperation>().Single(t => t.Name == Checkpoints);
        checkpoints.ForeignKeys.Select(f => (f.PrincipalTable, f.OnDelete)).Should().Equal(("siege_matches", ReferentialAction.Cascade));
        checkpoints.Columns.Select(c => c.Name).Should().Contain(new[] { "SiegeMatchId", "CapturedAt", "Substate", "Wave", "CompletedWave", "AliveEnemies", "QueuedEnemies", "PayloadJson", "Revision", "UpdatedAt" });
    }

    [Fact]
    public void Down_DropsExactlyTheThreeTables()
    {
        var ops = new AddSiegeSurvivalResults().DownOperations;
        ops.Should().OnlyContain(o => o is DropTableOperation);
        ops.OfType<DropTableOperation>().Select(t => t.Name).Should().BeEquivalentTo(Results, Players, Checkpoints);
    }
}
