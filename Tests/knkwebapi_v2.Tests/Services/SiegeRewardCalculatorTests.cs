using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using Xunit;
using static knkwebapi_v2.Services.SiegeRewardCalculator;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Siege Phase 6 reward matrix (docs/specs/siege-minigame/DESIGN.md §7.6): win / holding / capture
/// per participant, only for those still in the match at the end, for 2- and 3-team scenarios.
/// Reward amounts are the scenario defaults: win 100 coins / 10 XP / 1 gem, holding 50 / 5,
/// capture 50 / 5.
/// </summary>
public class SiegeRewardCalculatorTests
{
    // 2 teams: Defender 201 (alliance 1), Attacker 202 (alliance 2); objectives 501 (IV) and 502,
    // both held by the first Defender at the start.
    private static SiegeScenario TwoTeams()
    {
        var s = new SiegeScenario { Id = 100, Name = "Two teams" };
        s.Teams.Add(new SiegeTeam { Id = 201, SortOrder = 0, Role = SiegeTeamRole.Defender, AllianceGroup = 1 });
        s.Teams.Add(new SiegeTeam { Id = 202, SortOrder = 1, Role = SiegeTeamRole.Attacker, AllianceGroup = 2 });
        s.Objectives.Add(new SiegeObjective { Id = 501, SortOrder = 0, Name = "Keep", InstantVictory = true });
        s.Objectives.Add(new SiegeObjective { Id = 502, SortOrder = 1, Name = "Gatehouse" });
        return s;
    }

    // 3 teams: Defender 211 (alliance 1), Attackers 212 and 213 (allies, alliance 2);
    // objective 601 held by the Defender, 602 held by 213 from the start (explicit holder).
    private static SiegeScenario ThreeTeamsTwoAlliances()
    {
        var s = new SiegeScenario { Id = 110, Name = "Three teams" };
        s.Teams.Add(new SiegeTeam { Id = 211, SortOrder = 0, Role = SiegeTeamRole.Defender, AllianceGroup = 1 });
        s.Teams.Add(new SiegeTeam { Id = 212, SortOrder = 1, Role = SiegeTeamRole.Attacker, AllianceGroup = 2 });
        s.Teams.Add(new SiegeTeam { Id = 213, SortOrder = 2, Role = SiegeTeamRole.Attacker, AllianceGroup = 2 });
        s.Objectives.Add(new SiegeObjective { Id = 601, SortOrder = 0, Name = "Keep", InstantVictory = true });
        s.Objectives.Add(new SiegeObjective { Id = 602, SortOrder = 1, Name = "Mill", InitialHolderTeamId = 213 });
        return s;
    }

    private static Reward Reward(SiegeScenario s, int? winner, IEnumerable<ObjectiveEntry> entries, int userId, int teamId, bool present = true) =>
        For(new ParticipantInput(userId, teamId, present), s, winner, Outcomes(s, entries));

    // ---- 2 teams ----

    [Fact]
    public void TwoTeams_AttackersTakeBothObjectives_WinnersGetWinHoldingAndCapture()
    {
        var s = TwoTeams();
        var entries = new[]
        {
            new ObjectiveEntry(502, 202, 2), // user 2 captures the Gatehouse
            new ObjectiveEntry(501, 202, 3)  // user 3 captures the Keep (IV) -> attackers win
        };

        var capturer = Reward(s, 2, entries, userId: 2, teamId: 202);
        Assert.True(capturer.Won);
        Assert.Equal(2, capturer.HoldingCount);
        Assert.Equal(1, capturer.CaptureCount);
        Assert.Equal(100 + 2 * 50 + 50, capturer.Coins);
        Assert.Equal(10 + 2 * 5 + 5, capturer.Experience);
        Assert.Equal(1, capturer.Gems);

        var teammate = Reward(s, 2, entries, userId: 4, teamId: 202);
        Assert.Equal((true, 2, 0), (teammate.Won, teammate.HoldingCount, teammate.CaptureCount));
        Assert.Equal((200, 20, 1), (teammate.Coins, teammate.Experience, teammate.Gems));

        var loser = Reward(s, 2, entries, userId: 1, teamId: 201);
        Assert.Equal((false, 0, 0), (loser.Won, loser.HoldingCount, loser.CaptureCount));
        Assert.Equal((0, 0, 0), (loser.Coins, loser.Experience, loser.Gems));
    }

    [Fact]
    public void TwoTeams_LeftEarly_GetsNothing_EvenAfterCapturing()
    {
        var s = TwoTeams();
        var entries = new[] { new ObjectiveEntry(502, 202, 5), new ObjectiveEntry(501, 202, 3) };

        var leaver = Reward(s, 2, entries, userId: 5, teamId: 202, present: false);

        Assert.False(leaver.PresentAtEnd);
        Assert.False(leaver.Won);
        Assert.Equal((0, 0, 0, 0, 0), (leaver.HoldingCount, leaver.CaptureCount, leaver.Coins, leaver.Experience, leaver.Gems));
    }

    [Fact]
    public void TwoTeams_DefendersHoldToTheEnd_WinOnly_NoHoldingForObjectivesTheyAlreadyHeld()
    {
        var s = TwoTeams();
        var entries = new[] { new ObjectiveEntry(501, 201, null), new ObjectiveEntry(502, 201, null) };

        var defender = Reward(s, 1, entries, userId: 1, teamId: 201);
        Assert.Equal((true, 0, 0), (defender.Won, defender.HoldingCount, defender.CaptureCount));
        Assert.Equal((100, 10, 1), (defender.Coins, defender.Experience, defender.Gems));

        var attacker = Reward(s, 1, entries, userId: 2, teamId: 202);
        Assert.Equal((0, 0, 0), (attacker.Coins, attacker.Experience, attacker.Gems));
    }

    [Fact]
    public void TwoTeams_LosersStillGetHoldingAndCaptureRewards()
    {
        // Attackers take the side objective but the defenders win at time expiry.
        var s = TwoTeams();
        var entries = new[] { new ObjectiveEntry(502, 202, 2) };

        var attacker = Reward(s, 1, entries, userId: 2, teamId: 202);

        Assert.False(attacker.Won);
        Assert.Equal((1, 1), (attacker.HoldingCount, attacker.CaptureCount));
        Assert.Equal((100, 10, 0), (attacker.Coins, attacker.Experience, attacker.Gems));
    }

    [Fact]
    public void Recapture_PingPong_CapturePaidOncePerObjective_HoldingByFinalHolder()
    {
        var s = TwoTeams();
        var entries = new[]
        {
            new ObjectiveEntry(502, 202, 2), // A takes it
            new ObjectiveEntry(502, 201, 1), // D takes it back
            new ObjectiveEntry(502, 202, 2), // A again
            new ObjectiveEntry(502, 201, 1), // D again
            new ObjectiveEntry(502, 202, 2)  // A holds it at the end
        };

        var attacker = Reward(s, null, entries, userId: 2, teamId: 202);
        Assert.Equal((1, 1), (attacker.HoldingCount, attacker.CaptureCount));
        Assert.Equal((100, 10), (attacker.Coins, attacker.Experience));

        var defender = Reward(s, null, entries, userId: 1, teamId: 201);
        Assert.Equal((0, 1), (defender.HoldingCount, defender.CaptureCount));
        Assert.Equal((50, 5), (defender.Coins, defender.Experience));
    }

    [Fact]
    public void Draw_NoWinReward()
    {
        var s = TwoTeams();
        var r = Reward(s, null, Array.Empty<ObjectiveEntry>(), userId: 1, teamId: 201);
        Assert.False(r.Won);
        Assert.Equal((0, 0, 0), (r.Coins, r.Experience, r.Gems));
    }

    [Fact]
    public void Outcomes_UnreportedObjectiveKeepsItsInitialHolder_ExplicitHolderBeatsFirstDefender()
    {
        var s = ThreeTeamsTwoAlliances();
        var outcomes = Outcomes(s, new[] { new ObjectiveEntry(601, 212, 7), new ObjectiveEntry(999, 211, 1) });

        Assert.Equal(2, outcomes.Count); // the unknown objective 999 is ignored
        var keep = outcomes.Single(o => o.ObjectiveId == 601);
        Assert.Equal((211, 212), (keep.InitialHolderTeamId, keep.FinalHolderTeamId));
        var mill = outcomes.Single(o => o.ObjectiveId == 602);
        Assert.Equal((213, 213), (mill.InitialHolderTeamId, mill.FinalHolderTeamId));
        Assert.Empty(mill.CapturerUserIds);
    }

    // ---- 3 teams ----

    [Fact]
    public void ThreeTeams_AlliedAttackersWin_HoldingOnlyForTheTeamThatGainedTheObjective()
    {
        var s = ThreeTeamsTwoAlliances();
        var entries = new[] { new ObjectiveEntry(601, 212, 7) }; // team 212's user 7 takes the Keep

        var capturer = Reward(s, 2, entries, userId: 7, teamId: 212);
        Assert.Equal((true, 1, 1), (capturer.Won, capturer.HoldingCount, capturer.CaptureCount));
        Assert.Equal((200, 20, 1), (capturer.Coins, capturer.Experience, capturer.Gems));

        // The ally held the Mill from the start: a win, but no holding reward for it.
        var ally = Reward(s, 2, entries, userId: 8, teamId: 213);
        Assert.Equal((true, 0, 0), (ally.Won, ally.HoldingCount, ally.CaptureCount));
        Assert.Equal((100, 10, 1), (ally.Coins, ally.Experience, ally.Gems));

        var defender = Reward(s, 2, entries, userId: 9, teamId: 211);
        Assert.Equal((0, 0, 0), (defender.Coins, defender.Experience, defender.Gems));
    }

    [Fact]
    public void ThreeTeams_ThreeAlliances_OnlyTheWinningAllianceGetsTheWin()
    {
        var s = ThreeTeamsTwoAlliances();
        s.Teams.Single(t => t.Id == 213).AllianceGroup = 3; // everyone for themselves
        var entries = new[] { new ObjectiveEntry(601, 212, 7), new ObjectiveEntry(602, 212, 7) };

        var winner = Reward(s, 2, entries, userId: 7, teamId: 212);
        Assert.Equal((true, 2, 2), (winner.Won, winner.HoldingCount, winner.CaptureCount));
        Assert.Equal((100 + 100 + 100, 10 + 10 + 10, 1), (winner.Coins, winner.Experience, winner.Gems));

        var lostTheMill = Reward(s, 2, entries, userId: 8, teamId: 213);
        Assert.Equal((false, 0, 0), (lostTheMill.Won, lostTheMill.HoldingCount, lostTheMill.CaptureCount));
        Assert.Equal((0, 0, 0), (lostTheMill.Coins, lostTheMill.Experience, lostTheMill.Gems));
    }

    [Fact]
    public void ThreeTeams_DefenderRetakesAnAttackersStartingObjective_GetsHolding()
    {
        var s = ThreeTeamsTwoAlliances();
        var entries = new[] { new ObjectiveEntry(602, 211, 9) };

        var defender = Reward(s, 1, entries, userId: 9, teamId: 211);

        Assert.Equal((true, 1, 1), (defender.Won, defender.HoldingCount, defender.CaptureCount));
        Assert.Equal((200, 20, 1), (defender.Coins, defender.Experience, defender.Gems));
    }

    [Fact]
    public void NegativeConfiguredAmounts_CountAsZero()
    {
        var s = TwoTeams();
        s.CoinRewardWin = -100;
        s.ExpRewardCapture = -5;
        var entries = new[] { new ObjectiveEntry(501, 202, 2) };

        var r = Reward(s, 2, entries, userId: 2, teamId: 202);

        Assert.Equal(0 + 50 + 50, r.Coins);
        Assert.Equal(10 + 5 + 0, r.Experience);
    }

    [Fact]
    public void ParticipantWithoutTeam_OnlyCaptureRewards()
    {
        // The team was deleted after the match (SetNull) - rebuilding a stored breakdown must not throw.
        var s = TwoTeams();
        var r = For(new ParticipantInput(2, null, true), s, 2,
            Outcomes(s, new[] { new ObjectiveEntry(501, 202, 2) }));

        Assert.Equal((false, 0, 1), (r.Won, r.HoldingCount, r.CaptureCount));
        Assert.Equal((50, 5, 0), (r.Coins, r.Experience, r.Gems));
    }

    // ---- gate damage (PandiO/knk-workspace#165) and participation (#166); defaults: 10 coins / 1 XP per
    // 100 HP, participation 25 coins / 3 XP at 75 % active ----

    private static Reward Contribution(SiegeScenario s, int gateDamage, int active, int matchSeconds, bool present = true) =>
        For(new ParticipantInput(7, 201, present, gateDamage, active, matchSeconds), s, null, Outcomes(s, Array.Empty<ObjectiveEntry>()));

    [Fact]
    public void GateDamage_PaysPerFullStep()
    {
        var s = TwoTeams();

        var r = Contribution(s, gateDamage: 350, active: 0, matchSeconds: 0);

        Assert.Equal(3, r.GateDamageSteps);
        Assert.Equal((30, 3, 0), (r.Coins, r.Experience, r.Gems));
        Assert.Equal(0, Contribution(s, gateDamage: 99, active: 0, matchSeconds: 0).Coins);
    }

    [Fact]
    public void GateDamage_UsesTheScenarioStepAndAmounts_AStepBelowOnePaysNothing()
    {
        var s = TwoTeams();
        s.GateDamageRewardStep = 50;
        s.CoinRewardGateDamage = 4;
        s.ExpRewardGateDamage = 2;
        Assert.Equal((28, 14), (Contribution(s, 350, 0, 0).Coins, Contribution(s, 350, 0, 0).Experience));

        s.GateDamageRewardStep = 0;
        Assert.Equal((0, 0), (Contribution(s, 350, 0, 0).GateDamageSteps, Contribution(s, 350, 0, 0).Coins));
    }

    [Theory]
    [InlineData(450, 600, true)]   // exactly 75 %
    [InlineData(449, 600, false)]  // just below
    [InlineData(600, 600, true)]
    [InlineData(0, 600, false)]
    [InlineData(0, 0, false)]      // no reported match time: not paid
    public void Participation_NeedsTheMinimumActiveShare(int active, int matchSeconds, bool paid)
    {
        var r = Contribution(TwoTeams(), 0, active, matchSeconds);

        Assert.Equal(paid, r.Participation);
        Assert.Equal(paid ? (25, 3) : (0, 0), (r.Coins, r.Experience));
    }

    [Fact]
    public void Participation_ConfigurablePercent_ZeroPaysEveryoneStillThere()
    {
        var s = TwoTeams();
        s.ParticipationMinActivePercent = 50;
        Assert.True(Contribution(s, 0, 300, 600).Participation);
        Assert.False(Contribution(s, 0, 299, 600).Participation);

        s.ParticipationMinActivePercent = 0;
        Assert.True(Contribution(s, 0, 0, 0).Participation);
        Assert.False(Contribution(s, 0, 600, 600, present: false).Participation); // leavers never
    }

    [Fact]
    public void Leaver_GetsNoGateDamageOrParticipationReward()
    {
        var r = Contribution(TwoTeams(), gateDamage: 1000, active: 600, matchSeconds: 600, present: false);

        Assert.Equal((0, 0, 0, false), (r.Coins, r.Experience, r.GateDamageSteps, r.Participation));
    }

    [Fact]
    public void AllRewardKinds_AddUp()
    {
        var s = TwoTeams();
        var entries = new[] { new ObjectiveEntry(502, 202, 2), new ObjectiveEntry(501, 202, 3) };

        var r = For(new ParticipantInput(2, 202, true, GateDamage: 250, ActiveSeconds: 500, MatchSeconds: 600), s, 2, Outcomes(s, entries));

        // win 100/10/1, holding 2 x 50/5, capture 50/5, gate damage 2 x 10/1, participation 25/3
        Assert.Equal((100 + 100 + 50 + 20 + 25, 10 + 10 + 5 + 2 + 3, 1), (r.Coins, r.Experience, r.Gems));
    }

    [Fact]
    public void NegativeContributionAmounts_CountAsZero()
    {
        var s = TwoTeams();
        s.CoinRewardGateDamage = -10;
        s.ExpRewardParticipation = -3;

        var r = Contribution(s, 200, 600, 600);

        Assert.Equal((25, 2), (r.Coins, r.Experience));
    }
}
