using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Leaderboards;
using knkwebapi_v2.Services.Statistics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Statistics;

/// <summary>Arena duel statistics (KNG-221, arena duels DESIGN §10.3, D10, D17): wins, losses and draws in
/// context "duel", the void retraction, the projection-owned guard and the public-by-default visibility.</summary>
public class DuelStatisticsProjectorTests : IDisposable
{
    private static readonly TimeZoneInfo Amsterdam = StatisticsPeriods.FindZone("Europe/Amsterdam");
    private static readonly DateTime Ended = new(2026, 10, 2, 22, 30, 0, DateTimeKind.Utc); // 00:30 on Oct 3 in Amsterdam

    private readonly StatisticsTestDb _db = new();

    public void Dispose() => _db.Dispose();

    /// <summary>alice (1) challenges bob (2).</summary>
    private static DuelMatch Match(int id, DuelMatchStatus status, DuelEndReason? reason, int? winner,
        DuelReviewState review = DuelReviewState.None, int challenger = 1, int opponent = 2) => new()
        {
            Id = id, PublicToken = Guid.NewGuid().ToString(), ArenaId = 1, ChallengerUserId = challenger, OpponentUserId = opponent,
            Status = status, EndReason = reason, WinnerUserId = winner, Review = review,
            EndedAt = status is DuelMatchStatus.Created or DuelMatchStatus.InProgress ? null : Ended
        };

    private static Dictionary<(int, string), decimal> Project(DuelMatch match)
    {
        var deltas = new StatisticsDeltaSet();
        DuelStatisticsProjector.Project(match, deltas, Amsterdam, null, 1);
        Assert.All(deltas.Daily.Keys, k => Assert.Equal((new DateOnly(2026, 10, 3), "duel"), (k.Day, k.ContextKey)));
        return deltas.Totals.ToDictionary(t => (t.Key.UserId, t.Key.MetricKey), t => t.Value.Value);
    }

    [Fact]
    public void Win_IsAWinForTheWinner_AndALossForTheOther()
    {
        var totals = Project(Match(1, DuelMatchStatus.Completed, DuelEndReason.Win, 2));

        Assert.Equal(new Dictionary<(int, string), decimal> { [(2, "wins")] = 1, [(1, "losses")] = 1 }, totals);
    }

    [Fact]
    public void Forfeit_IsALossForTheQuitter()
    {
        var totals = Project(Match(1, DuelMatchStatus.Completed, DuelEndReason.Forfeit, 1));

        Assert.Equal(new Dictionary<(int, string), decimal> { [(1, "wins")] = 1, [(2, "losses")] = 1 }, totals);
    }

    [Theory]
    [InlineData(DuelEndReason.TimeLimit)]
    [InlineData(DuelEndReason.AgreedDraw)]
    [InlineData(DuelEndReason.SimultaneousDeath)]
    public void Draws_CountForBoth(DuelEndReason reason)
    {
        var totals = Project(Match(1, DuelMatchStatus.Completed, reason, null));

        Assert.Equal(new Dictionary<(int, string), decimal> { [(1, "draws")] = 1, [(2, "draws")] = 1 }, totals);
    }

    [Theory]
    [InlineData(DuelMatchStatus.Cancelled, DuelEndReason.TeleportFailed)]
    [InlineData(DuelMatchStatus.Cancelled, DuelEndReason.PlayerLeftLobby)]
    [InlineData(DuelMatchStatus.Cancelled, DuelEndReason.NoBattleground)]
    [InlineData(DuelMatchStatus.Cancelled, DuelEndReason.AdminCancelled)]
    [InlineData(DuelMatchStatus.Aborted, DuelEndReason.ServerRestart)]
    [InlineData(DuelMatchStatus.Aborted, DuelEndReason.ApiOutage)]
    public void CancelledAndAborted_CountNothing(DuelMatchStatus status, DuelEndReason reason)
    {
        Assert.Empty(Project(Match(1, status, reason, null)));
        Assert.False(DuelStatisticsProjector.Counts(Match(1, status, reason, null)));
    }

    [Fact]
    public void ADecisiveReasonWithoutAWinner_CountsNothing()
    {
        Assert.Empty(Project(Match(1, DuelMatchStatus.Completed, DuelEndReason.Win, null)));
    }

    [Theory]
    [InlineData(DuelReviewState.None, true)]
    [InlineData(DuelReviewState.Flagged, true)]
    [InlineData(DuelReviewState.Released, true)]
    [InlineData(DuelReviewState.Held, false)]
    [InlineData(DuelReviewState.Voided, false)]
    public void Review_HeldWaits_VoidedNeverCounts(DuelReviewState review, bool counts)
    {
        Assert.Equal(counts, DuelStatisticsProjector.Counts(Match(1, DuelMatchStatus.Completed, DuelEndReason.Win, 1, review)));
    }

    private void Seed(params DuelMatch[] matches)
    {
        _db.Context.DuelMatches.AddRange(matches);
        _db.Context.SaveChanges();
    }

    private void SetReview(int matchId, DuelReviewState review)
    {
        var match = _db.Context.DuelMatches.Single(m => m.Id == matchId);
        match.Review = review;
        _db.Context.SaveChanges();
        _db.Context.ChangeTracker.Clear();
    }

    private List<(string, long)> Sources() => _db.Context.StatisticsProjectedSources.AsNoTracking()
        .OrderBy(s => s.SourceType).ThenBy(s => s.SourceId).Select(s => new { s.SourceType, s.SourceId }).AsEnumerable()
        .Select(s => (s.SourceType, s.SourceId)).ToList();

    [Fact]
    public async Task ProjectNext_CountsEachDecidedMatchOnce()
    {
        Seed(
            Match(1, DuelMatchStatus.Completed, DuelEndReason.Win, 1),
            Match(2, DuelMatchStatus.Completed, DuelEndReason.Forfeit, 2, DuelReviewState.Flagged),
            Match(3, DuelMatchStatus.Completed, DuelEndReason.AgreedDraw, null),
            Match(4, DuelMatchStatus.Cancelled, DuelEndReason.PlayerLeftLobby, null),
            Match(5, DuelMatchStatus.Aborted, DuelEndReason.ServerRestart, null),
            Match(6, DuelMatchStatus.InProgress, null, null),
            Match(7, DuelMatchStatus.Created, null, null));

        Assert.Equal(3, await _db.DuelProjector().ProjectNextAsync());
        Assert.Equal(0, await _db.DuelProjector().ProjectNextAsync());

        Assert.Equal(1m, _db.Total(1, "wins", "duel"));
        Assert.Equal(1m, _db.Total(1, "losses", "duel"));
        Assert.Equal(1m, _db.Total(1, "draws", "duel"));
        Assert.Equal(1m, _db.Total(2, "wins", "duel"));
        Assert.Equal(1m, _db.Total(2, "losses", "duel"));
        Assert.Equal(1m, _db.Total(2, "draws", "duel"));
        Assert.Equal(new DateOnly(2026, 10, 3), _db.Daily(1, "wins").Single().Day);
        Assert.Equal(new[] { ("duel_match", 1L), ("duel_match", 2L), ("duel_match", 3L) }, Sources());
    }

    [Fact]
    public async Task AHeldResult_CountsOnceReleased_AndAVoidedHoldNever()
    {
        Seed(
            Match(1, DuelMatchStatus.Completed, DuelEndReason.Win, 1, DuelReviewState.Held),
            Match(2, DuelMatchStatus.Completed, DuelEndReason.Win, 1, DuelReviewState.Held));

        Assert.Equal(0, await _db.DuelProjector().ProjectNextAsync());
        Assert.Null(_db.Total(1, "wins", "duel"));

        SetReview(1, DuelReviewState.Released);
        SetReview(2, DuelReviewState.Voided);
        Assert.Equal(1, await _db.DuelProjector().ProjectNextAsync());
        Assert.Equal(0, await _db.DuelProjector().ProjectNextAsync());

        Assert.Equal(1m, _db.Total(1, "wins", "duel"));
        Assert.Equal(1m, _db.Total(2, "losses", "duel"));
        Assert.Equal(new[] { ("duel_match", 1L) }, Sources()); // the voided hold was never counted, so nothing to retract
    }

    [Fact]
    public async Task ARevertedOrConfiscatedResult_IsTakenBackOutOnce()
    {
        Seed(
            Match(1, DuelMatchStatus.Completed, DuelEndReason.Win, 1),
            Match(2, DuelMatchStatus.Completed, DuelEndReason.Win, 1, DuelReviewState.Flagged));
        await _db.DuelProjector().ProjectNextAsync();
        Assert.Equal(2m, _db.Total(1, "wins", "duel"));

        SetReview(2, DuelReviewState.Voided); // staff reverted it (DESIGN §10.3)
        Assert.Equal(1, await _db.DuelProjector().ProjectNextAsync());
        Assert.Equal(0, await _db.DuelProjector().ProjectNextAsync());

        Assert.Equal(1m, _db.Total(1, "wins", "duel"));
        Assert.Equal(1m, _db.Total(2, "losses", "duel"));
        Assert.Equal(1m, _db.Daily(1, "wins").Single().Value); // taken off the day it was counted on
        Assert.Equal(new[] { ("duel_match", 1L), ("duel_match", 2L), ("duel_match_retracted", 2L) }, Sources());
    }

    [Fact]
    public async Task Rebuild_ReprojectsWhatCountsNow()
    {
        Seed(
            Match(1, DuelMatchStatus.Completed, DuelEndReason.Win, 1),
            Match(2, DuelMatchStatus.Completed, DuelEndReason.Win, 2),
            Match(3, DuelMatchStatus.Completed, DuelEndReason.TimeLimit, null),
            Match(4, DuelMatchStatus.Completed, DuelEndReason.Win, 1, challenger: 1, opponent: 3));
        await _db.DuelProjector().ProjectNextAsync();
        SetReview(2, DuelReviewState.Voided);
        await _db.DuelProjector().ProjectNextAsync();

        await _db.DuelProjector().RebuildAsync(1);
        Assert.Equal(2m, _db.Total(1, "wins", "duel"));
        Assert.Null(_db.Total(1, "losses", "duel"));
        Assert.Equal(1m, _db.Total(1, "draws", "duel"));
        Assert.Equal(1m, _db.Total(3, "losses", "duel")); // other users untouched

        await _db.DuelProjector().RebuildAsync(null);
        Assert.Null(_db.Total(1, "wins", "duel"));
        Assert.Empty(Sources());
        Assert.Equal(3, await _db.DuelProjector().ProjectNextAsync());
        Assert.Equal(2m, _db.Total(1, "wins", "duel"));
        Assert.Null(_db.Total(2, "wins", "duel"));
        Assert.Equal(1m, _db.Total(2, "draws", "duel"));
        Assert.Equal(1m, _db.Total(3, "losses", "duel"));
    }

    [Fact]
    public async Task RebuildService_AcceptsDuel()
    {
        Seed(Match(1, DuelMatchStatus.Completed, DuelEndReason.Win, 1));
        var rebuild = new StatisticsRebuildService(_db.LedgerProjector(), _db.SiegeProjector(), _db.DuelProjector());

        Assert.Equal(1, await rebuild.RebuildAsync("duel", null));
        Assert.Equal(1m, _db.Total(1, "wins", "duel"));
    }

    [Fact]
    public async Task ProjectionCycle_RunsTheDuelProjector()
    {
        Seed(Match(1, DuelMatchStatus.Completed, DuelEndReason.Win, 1));
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<knkwebapi_v2.Repositories.Interfaces.IStatisticsRepository>(services, _ => _db.Repository());
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped(services, _ => _db.LedgerProjector());
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped(services, _ => _db.SiegeProjector());
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped(services, _ => _db.DuelProjector());
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var service = new StatisticsProjectionService(provider, Microsoft.Extensions.Logging.Abstractions.NullLogger<StatisticsProjectionService>.Instance);

        await service.RunCycleAsync(StatisticsTestDb.Now);

        Assert.Equal(1m, _db.Total(1, "wins", "duel"));
    }

    // ---- the plugin can't write duel results (projection-owned)

    [Fact]
    public async Task PluginPostedDuelResults_AreRefused_ButDuelKillsAndDeathsAreAccepted()
    {
        var at = new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);
        var batch = new StatisticsBatchDto
        {
            BatchId = Guid.NewGuid(),
            Counters = new()
            {
                new() { UserId = 1, Metric = "wins", Context = "duel", Value = 1, OccurredAt = at },
                new() { UserId = 1, Metric = "losses", Context = "duel", Value = 1, OccurredAt = at },
                new() { UserId = 1, Metric = "draws", Context = "duel", Value = 1, OccurredAt = at },
                new() { UserId = 1, Metric = "deaths", Context = "duel", Value = 1, OccurredAt = at },
            },
            PvpKills = new()
            {
                new() { KillerUserId = 1, VictimUserId = 2, Context = "duel", OccurredAt = at },
            }
        };

        var result = await _db.Ingestion().IngestAsync(batch);

        Assert.Equal(new[] { ("counters", 0, "NotPluginWritable"), ("counters", 1, "NotPluginWritable"), ("counters", 2, "NotPluginWritable") },
            result.Rejected.Select(r => (r.Section, r.Index, r.Code)));
        Assert.Null(_db.Total(1, "wins", "duel"));
        Assert.Equal(1m, _db.Total(1, "deaths", "duel"));
        Assert.Equal(1m, _db.Total(1, "pvp_kills", "duel"));
    }

    // ---- visibility: duel W/L/D are public by default (D17)

    private static PlayerStatVisibility Row(string setting, StatisticVisibility visibility, string context = "") =>
        new() { UserId = 1, SettingKey = setting, ContextKey = context, Visibility = visibility };

    [Fact]
    public void Rules_DuelResultsArePublicByDefault_AndThePlayersChoiceWins()
    {
        var none = Array.Empty<PlayerStatVisibility>();
        foreach (var setting in new[] { "wins", "losses", "draws" })
        {
            Assert.True(StatisticsVisibilityRules.IsPublic(none, setting, "duel"));
            Assert.False(StatisticsVisibilityRules.IsPublic(none, setting, "siege"));
            Assert.False(StatisticsVisibilityRules.IsPublic(none, setting)); // the total stays private
        }
        Assert.False(StatisticsVisibilityRules.IsPublic(none, "pvp_kills", "duel")); // only W/L/D

        Assert.False(StatisticsVisibilityRules.IsPublic(new[] { Row("wins", StatisticVisibility.Nobody, "duel") }, "wins", "duel"));
        Assert.False(StatisticsVisibilityRules.IsPublic(new[] { Row("wins", StatisticVisibility.Friends, "duel") }, "wins", "duel"));
        // A metric-level choice the player made applies to the duel context too.
        Assert.False(StatisticsVisibilityRules.IsPublic(new[] { Row("wins", StatisticVisibility.Nobody) }, "wins", "duel"));
        Assert.True(StatisticsVisibilityRules.IsPublic(
            new[] { Row("wins", StatisticVisibility.Nobody), Row("wins", StatisticVisibility.Everyone, "duel") }, "wins", "duel"));
    }

    [Fact]
    public async Task OtherPlayers_SeeDuelResults_UntilThePlayerHidesThem()
    {
        Seed(Match(1, DuelMatchStatus.Completed, DuelEndReason.Win, 1), Match(2, DuelMatchStatus.Completed, DuelEndReason.Win, 2));
        await _db.DuelProjector().ProjectNextAsync();
        var bob = new StatisticsViewer(StatisticsViewerKind.SignedIn, 2);

        var dto = await _db.Query().GetAsync(1, bob, null, null);
        var wins = dto!.Metrics.Single(m => m.Key == "wins");
        Assert.Null(wins.Value);
        Assert.Equal(new[] { ("duel", 1m) }, wins.Contexts!.Select(c => (c.Context, c.Value)));
        Assert.Contains(dto.Metrics, m => m.Key == "losses");

        var settings = await _db.Visibility().GetAsync(1);
        var winsSetting = settings!.Settings.Single(s => s.SettingKey == "wins");
        Assert.Equal(StatisticVisibility.Nobody, winsSetting.Visibility);
        Assert.Equal(new[] { ("duel", StatisticVisibility.Everyone, false) },
            winsSetting.Contexts.Select(c => (c.Context, c.Visibility, c.IsOverride)));

        // The player hides duel wins in the normal settings (expected = the shown default).
        await _db.Visibility().UpdateAsync(1, new StatisticsVisibilityUpdateDto
        {
            Changes = new() { new() { SettingKey = "wins", Context = "duel", Expected = StatisticVisibility.Everyone, Visibility = StatisticVisibility.Nobody } }
        });
        _db.Context.ChangeTracker.Clear();

        dto = await _db.Query().GetAsync(1, bob, null, null);
        Assert.DoesNotContain(dto!.Metrics, m => m.Key == "wins");
        Assert.Contains(dto.Metrics, m => m.Key == "losses");
    }

    [Fact]
    public void Leaderboards_HaveNoDuelBoardsYet()
    {
        Assert.DoesNotContain(LeaderboardCatalog.Boards, b => b.Context == StatisticsCatalog.Duel);
        Assert.Contains(StatisticsCatalog.Duel, StatisticsCatalog.KnownContexts);
    }
}
