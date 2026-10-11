using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Statistics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Statistics;

/// <summary>Siege match projection (DESIGN.md §F.6, D1, L1-7/L1-8; link 2 acceptance criterion 5).</summary>
public class SiegeStatisticsProjectorTests : IDisposable
{
    private static readonly TimeZoneInfo Amsterdam = StatisticsPeriods.FindZone("Europe/Amsterdam");
    private static readonly DateTime Ended = new(2026, 10, 2, 22, 30, 0, DateTimeKind.Utc); // 00:30 on Oct 3 in Amsterdam

    private readonly StatisticsTestDb _db = new();
    private readonly SiegeTeam _red = new() { Id = 1, AllianceGroup = 1 };
    private readonly SiegeTeam _blue = new() { Id = 2, AllianceGroup = 2 };

    public void Dispose() => _db.Dispose();

    private SiegeMatchParticipant P(int userId, SiegeTeam? team, int kills = 0, int deaths = 0, int streak = 0, int captures = 0,
        DateTime? leftAt = null) => new()
        {
            UserId = userId, SiegeTeam = team, SiegeTeamId = team?.Id, Kills = kills, Deaths = deaths,
            HighestKillStreak = streak, Captures = captures, LeftAt = leftAt
        };

    private static SiegeMatch Match(int id, SiegeMatchStatus status, int? winner, params SiegeMatchParticipant[] participants)
    {
        var match = new SiegeMatch { Id = id, SiegeLobbyId = 1, SiegeScenarioId = 1, Status = status, EndedAt = Ended, WinningAllianceGroup = winner };
        foreach (var p in participants)
        {
            p.SiegeMatchId = id;
            match.Participants.Add(p);
        }
        return match;
    }

    private static Dictionary<(int, string), decimal> Project(SiegeMatch match)
    {
        var deltas = new StatisticsDeltaSet();
        SiegeStatisticsProjector.Project(match, deltas, Amsterdam, null);
        Assert.All(deltas.Daily.Keys, k => Assert.Equal((new DateOnly(2026, 10, 3), "siege"), (k.Day, k.ContextKey)));
        return deltas.Totals.ToDictionary(t => (t.Key.UserId, t.Key.MetricKey), t => t.Value.Value);
    }

    [Fact]
    public void Completed_WinnersWin_OthersLose_AndInMatchStatsCount()
    {
        var totals = Project(Match(1, SiegeMatchStatus.Completed, 1,
            P(1, _red, kills: 5, deaths: 2, streak: 3, captures: 1),
            P(2, _blue, kills: 1, deaths: 4, streak: 1),
            P(3, _red, leftAt: Ended))); // reported at the end (LeftAt == EndedAt) = present

        Assert.Equal(1m, totals[(1, "wins")]);
        Assert.Equal(1m, totals[(2, "losses")]);
        Assert.Equal(1m, totals[(3, "wins")]);
        Assert.Equal(5m, totals[(1, "pvp_kills")]);
        Assert.Equal(2m, totals[(1, "deaths")]);
        Assert.Equal(3m, totals[(1, "highest_killstreak")]);
        Assert.Equal(1m, totals[(1, "objectives_captured")]);
        Assert.False(totals.ContainsKey((3, "pvp_kills"))); // zero values write nothing
    }

    [Fact]
    public void NoWinner_IsADrawForPresentPlayers()
    {
        var totals = Project(Match(1, SiegeMatchStatus.Completed, null, P(1, _red), P(2, _blue)));

        Assert.Equal(1m, totals[(1, "draws")]);
        Assert.Equal(1m, totals[(2, "draws")]);
    }

    [Fact]
    public void LeavingEarly_IsALoss_WhateverTheOutcome()
    {
        var early = Ended.AddMinutes(-5);
        var win = Project(Match(1, SiegeMatchStatus.Completed, 1, P(1, _red, kills: 2, leftAt: early)));
        var draw = Project(Match(2, SiegeMatchStatus.Completed, null, P(1, _red, leftAt: early)));
        var noTeam = Project(Match(3, SiegeMatchStatus.Completed, 1, P(1, null, leftAt: early)));

        Assert.Equal(1m, win[(1, "losses")]);
        Assert.False(win.ContainsKey((1, "wins")));
        Assert.Equal(2m, win[(1, "pvp_kills")]); // the leaver's reported kills still count
        Assert.Equal(1m, draw[(1, "losses")]);
        Assert.Equal(1m, noTeam[(1, "losses")]);
    }

    [Fact]
    public void Aborted_HasNoResult_ButInMatchStatsCount()
    {
        var totals = Project(Match(1, SiegeMatchStatus.Aborted, null, P(1, _red, kills: 3, deaths: 1, captures: 2)));

        Assert.False(totals.Keys.Any(k => k.Item2 is "wins" or "losses" or "draws"));
        Assert.Equal(3m, totals[(1, "pvp_kills")]);
        Assert.Equal(2m, totals[(1, "objectives_captured")]);
    }

    [Fact]
    public void DeletedTeam_GivesNoResult()
    {
        var totals = Project(Match(1, SiegeMatchStatus.Completed, 1, P(1, null, kills: 1)));

        Assert.False(totals.Keys.Any(k => k.Item2 is "wins" or "losses" or "draws"));
        Assert.Equal(1m, totals[(1, "pvp_kills")]);
    }

    [Fact]
    public void LeftAndRejoined_CountsOnce_AsALoss()
    {
        var totals = Project(Match(1, SiegeMatchStatus.Completed, 1,
            P(1, _red, kills: 2, streak: 2, leftAt: Ended.AddMinutes(-10)),
            P(1, _red, kills: 3, streak: 4)));

        Assert.Equal(1m, totals[(1, "losses")]);
        Assert.False(totals.ContainsKey((1, "wins")));
        Assert.Equal(5m, totals[(1, "pvp_kills")]);
        Assert.Equal(4m, totals[(1, "highest_killstreak")]);
    }

    private void Seed(params SiegeMatch[] matches)
    {
        _db.Context.SiegeTeams.AddRange(_red, _blue);
        _db.Context.SiegeMatches.AddRange(matches);
        _db.Context.SaveChanges();
    }

    [Fact]
    public async Task ProjectNext_ProjectsEachEndedMatchOnce()
    {
        Seed(
            Match(1, SiegeMatchStatus.Completed, 1, P(1, _red, kills: 2), P(2, _blue)),
            Match(2, SiegeMatchStatus.Aborted, null, P(1, _red, kills: 1)),
            new SiegeMatch { Id = 3, SiegeLobbyId = 1, SiegeScenarioId = 1, Status = SiegeMatchStatus.InProgress });

        Assert.Equal(2, await _db.SiegeProjector().ProjectNextAsync());
        Assert.Equal(0, await _db.SiegeProjector().ProjectNextAsync());

        Assert.Equal(3m, _db.Total(1, "pvp_kills", "siege"));
        Assert.Equal(1m, _db.Total(1, "wins", "siege"));
        Assert.Equal(1m, _db.Total(2, "losses", "siege"));
        Assert.Equal(new long[] { 1, 2 }, _db.Context.StatisticsProjectedSources.AsNoTracking().Select(s => s.SourceId).OrderBy(i => i));
    }

    [Fact]
    public async Task Rebuild_ReprojectsTheSameValues()
    {
        Seed(Match(1, SiegeMatchStatus.Completed, 2, P(1, _red, kills: 2, streak: 2), P(2, _blue, kills: 4, streak: 3)));
        await _db.SiegeProjector().ProjectNextAsync();

        await _db.SiegeProjector().RebuildAsync(1);
        Assert.Equal(2m, _db.Total(1, "pvp_kills", "siege"));
        Assert.Equal(1m, _db.Total(1, "losses", "siege"));
        Assert.Equal(4m, _db.Total(2, "pvp_kills", "siege"));

        await _db.SiegeProjector().RebuildAsync(null);
        Assert.Null(_db.Total(2, "pvp_kills", "siege"));
        Assert.Equal(1, await _db.SiegeProjector().ProjectNextAsync());
        Assert.Equal(4m, _db.Total(2, "pvp_kills", "siege"));
        Assert.Equal(3m, _db.Total(2, "highest_killstreak", "siege"));
        Assert.Equal(1m, _db.Total(2, "wins", "siege"));
    }
}
