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
}
