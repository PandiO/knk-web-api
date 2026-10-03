using System.Text.Json;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Privacy;
using knkwebapi_v2.Tests.Services.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Privacy;

/// <summary>
/// GDPR erasure (DESIGN.md §F.14 + link 5's additions; link 6 acceptance criterion 4): request →
/// due date, dry run, execution deletes exactly the scope per table (the player's and merged
/// accounts' rows; other players' rows stay; kill pairs as killer or victim), pseudonymizes the
/// account, keeps the ledger and Siege rows, is idempotent; the due-date job respects its switch
/// and lead days.
/// </summary>
public class PrivacyDeletionServiceTests : IDisposable
{
    private static readonly DateTime Now = TelemetryTestDb.Now;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);
    private readonly TelemetryTestDb _db = new();

    public PrivacyDeletionServiceTests()
    {
        var c = _db.Context;
        // User 3 was merged into user 1 (MERGE_FORFEIT ledger leg, sourced from user 1).
        c.CurrencyTransactions.Add(Tx("merge", CurrencyReasons.MergeForfeit, "User", "1", 3));
        // A reward of user 1: the ledger is kept.
        c.CurrencyTransactions.Add(Tx("reward", "SIEGE_REWARD", null, null, 1));
        c.SiegeMatches.Add(new SiegeMatch { Id = 4, SiegeLobbyId = 1, SiegeScenarioId = 1, Status = SiegeMatchStatus.Completed });
        c.SiegeMatchParticipants.Add(new SiegeMatchParticipant { SiegeMatchId = 4, UserId = 1, Kills = 2 });

        foreach (var user in new[] { 1, 2, 3 })
        {
            c.PlayerStatDailies.Add(new PlayerStatDaily { UserId = user, Day = Today, MetricKey = "pvp_kills", ContextKey = "", Value = 1 });
            c.PlayerStatDailies.Add(new PlayerStatDaily { UserId = user, Day = Today, MetricKey = "pvp_kills.ranked", ContextKey = "open_world", Value = 1 });
            c.PlayerStatTotals.Add(new PlayerStatTotal { UserId = user, MetricKey = "pvp_kills", ContextKey = "", Value = 1 });
            c.PlayerStatSessions.Add(new PlayerStatSession { SessionKey = Guid.NewGuid(), UserId = user, StartedAt = Now, LastHeartbeatAt = Now });
            c.PlayerStatVisibilities.Add(new PlayerStatVisibility { UserId = user, SettingKey = "pvp_kills", ContextKey = "" });
            c.PlayerStatProfiles.Add(new PlayerStatProfile { UserId = user, LeaderboardExcluded = user == 1, LeaderboardExcludedReason = "x" });
            c.PlayerTitleChanges.Add(new PlayerTitleChange { UserId = user, ToTitleName = "Squire", CurrencyEntryId = 1000 + user, ChangedAt = Now });
            c.UserDomainDiscoveries.Add(new UserDomainDiscovery { UserId = user, DomainId = 10 + user });
            c.TelemetryEvents.Add(TelemetryTestDb.Stored("session.join", user, Now));
        }
        c.TelemetryEvents.Add(TelemetryTestDb.Stored("api.request_failed", null, Now));
        c.TelemetryEnhancedTargets.Add(new TelemetryEnhancedTarget { UserId = 1, ExpiresAt = Now.AddHours(1) });
        c.PlayerPvpKillPairDailies.AddRange(
            new PlayerPvpKillPairDaily { KillerUserId = 1, VictimUserId = 2, Day = Today, ContextKey = "", Count = 1 },
            new PlayerPvpKillPairDaily { KillerUserId = 2, VictimUserId = 1, Day = Today, ContextKey = "", Count = 1 },
            new PlayerPvpKillPairDaily { KillerUserId = 2, VictimUserId = 3, Day = Today, ContextKey = "", Count = 1 },
            new PlayerPvpKillPairDaily { KillerUserId = 2, VictimUserId = 2, Day = Today, ContextKey = "x", Count = 1 });
        var snapshot = new LeaderboardSnapshot { Id = 1, BoardKey = "pvp_kills", Period = LeaderboardPeriod.Lifetime, IsCurrent = true, EntryCount = 2 };
        snapshot.Entries.Add(new LeaderboardSnapshotEntry { UserId = 1, Rank = 1, Value = 5 });
        snapshot.Entries.Add(new LeaderboardSnapshotEntry { UserId = 2, Rank = 2, Value = 4 });
        c.LeaderboardSnapshots.Add(snapshot);
        c.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static CurrencyTransaction Tx(string id, string reason, string? sourceType, string? sourceRef, int userId) => new()
    {
        PublicId = id, Kind = CurrencyTransactionKind.Grant, ReasonCode = reason, Reason = reason, SourceType = sourceType,
        SourceRef = sourceRef, IdempotencyScope = "t", IdempotencyKey = id, RequestHash = "h", CreatedAt = Now.AddDays(-10),
        Entries = new List<CurrencyEntry>
        {
            new() { Currency = Currency.Coins, AccountKind = CurrencyAccountKind.User, UserId = userId, Operation = CurrencyOperation.Add,
                    Amount = 5, BalanceBefore = 0, BalanceAfter = 5 }
        }
    };

    private async Task<int> RequestFor(int userId = 1)
    {
        var result = await _db.Privacy().RequestAsync(9, userId, " from email ");
        Assert.Equal(PrivacyOutcome.Ok, result.Outcome);
        return result.Request!.Id;
    }

    [Fact]
    public async Task Request_IsDueIn30Days_Audited_AndOnlyOncePending()
    {
        var result = await _db.Privacy().RequestAsync(9, 1, " from email ");

        Assert.Equal(PrivacyOutcome.Ok, result.Outcome);
        Assert.Equal((Now.AddDays(30), PrivacyRequestStatus.Pending, "from email"), (result.Request!.DueAt, result.Request.Status, result.Request.Note));
        Assert.Equal(Now.AddDays(27), result.Request.AutoExecuteAt);
        _db.Audit.Verify(a => a.RecordAsync(9, 1, AuditAction.PrivacyDeletionRequested, It.IsAny<string>()), Times.Once);

        var again = await _db.Privacy().RequestAsync(9, 1, null);
        Assert.Equal((PrivacyOutcome.AlreadyPending, result.Request.Id), (again.Outcome, again.Request!.Id));
        Assert.Equal(PrivacyOutcome.UserNotFound, (await _db.Privacy().RequestAsync(9, 99, null)).Outcome);
    }

    [Fact]
    public async Task DryRun_CountsTheScope_AndChangesNothing()
    {
        var id = await RequestFor();

        var preview = await _db.Privacy().ExecuteAsync(id, 9, dryRun: true);

        var result = preview.Request!.Result!;
        Assert.True(result.DryRun);
        Assert.Equal(new[] { 1, 3 }, result.UserIds);
        Assert.Equal(4, result.Deleted["player_stat_daily"]);
        Assert.Equal(3, result.Deleted["player_pvp_kill_pairs_daily"]);
        Assert.Equal(PrivacyRequestStatus.Pending, preview.Request.Status);
        Assert.Equal((6, 3, 3), (_db.NewContext().PlayerStatDailies.Count(), _db.NewContext().PlayerStatTotals.Count(), _db.NewContext().TelemetryEvents.Count(e => e.UserId != null)));
        Assert.Equal("alice", _db.NewContext().Users.Single(u => u.Id == 1).Username);
    }

    [Fact]
    public async Task Execute_DeletesExactlyTheScope_PerTable()
    {
        var id = await RequestFor();

        var executed = await _db.Privacy().ExecuteAsync(id, 9, dryRun: false);

        var c = _db.NewContext();
        int[] erased = { 1, 3 };
        Assert.Equal(PrivacyRequestStatus.Completed, executed.Request!.Status);
        // Every per-user table: only bob's rows are left.
        Assert.Equal(new[] { 2, 2 }, c.PlayerStatDailies.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 2 }, c.PlayerStatTotals.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 2 }, c.PlayerStatSessions.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 2 }, c.PlayerStatVisibilities.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 2 }, c.PlayerStatProfiles.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 2 }, c.PlayerTitleChanges.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 2 }, c.UserDomainDiscoveries.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 2 }, c.LeaderboardSnapshotEntries.Select(r => r.UserId).ToArray());
        Assert.Empty(c.TelemetryEnhancedTargets);
        // Diagnostic events: bob's and the anonymous API event stay.
        Assert.Equal(new int?[] { null, 2 }, c.TelemetryEvents.Select(e => e.UserId).OrderBy(u => u).ToArray());
        // Kill pairs as killer or victim are gone; bob vs bob (context x) stays.
        var pair = Assert.Single(c.PlayerPvpKillPairDailies);
        Assert.Equal((2, 2), (pair.KillerUserId, pair.VictimUserId));
        Assert.DoesNotContain(c.PlayerPvpKillPairDailies, p => erased.Contains(p.KillerUserId) || erased.Contains(p.VictimUserId));
    }

    [Fact]
    public async Task Execute_PseudonymizesTheAccounts_AndKeepsLedgerAndSiege()
    {
        var id = await RequestFor();

        var executed = await _db.Privacy().ExecuteAsync(id, 9, dryRun: false);

        var c = _db.NewContext();
        var alice = c.Users.Single(u => u.Id == 1);
        Assert.Equal(("deleted-1", (string?)null, (string?)null, (string?)null, false), (alice.Username, alice.Email, alice.Uuid, alice.PasswordHash, alice.IsActive));
        Assert.Equal((Now, "GDPR erasure", (Gender?)null), (alice.DeletedAt, alice.DeletedReason, alice.Gender));
        Assert.Equal("deleted-3", c.Users.Single(u => u.Id == 3).Username);
        Assert.Equal("bob", c.Users.Single(u => u.Id == 2).Username);
        Assert.Equal(2, c.CurrencyTransactions.Count());
        Assert.Equal(1, c.SiegeMatchParticipants.Count(p => p.UserId == 1));
        Assert.Equal((2, "deleted-1"), (executed.Request!.Result!.PseudonymizedUsers, executed.Request.Username));
        _db.Audit.Verify(a => a.RecordAsync(9, 1, AuditAction.PrivacyDeletionExecuted, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Execute_IsIdempotent_AndCancelledRequestsAreRefused()
    {
        var id = await RequestFor();
        var first = await _db.Privacy().ExecuteAsync(id, 9, dryRun: false);
        _db.Clock = Now.AddDays(1);

        var second = await _db.Privacy().ExecuteAsync(id, 9, dryRun: false);

        Assert.Equal(PrivacyOutcome.Ok, second.Outcome);
        Assert.Equal(first.Request!.ExecutedAt, second.Request!.ExecutedAt);
        Assert.Equal(JsonSerializer.Serialize(first.Request.Result), JsonSerializer.Serialize(second.Request.Result));
        _db.Audit.Verify(a => a.RecordAsync(9, 1, AuditAction.PrivacyDeletionExecuted, It.IsAny<string>()), Times.Once);
        Assert.Equal(PrivacyOutcome.NotPending, (await _db.Privacy().CancelAsync(9, id)).Outcome);

        var bobs = await RequestFor(2);
        Assert.Equal(PrivacyRequestStatus.Cancelled, (await _db.Privacy().CancelAsync(9, bobs)).Request!.Status);
        Assert.Equal(PrivacyOutcome.Ok, (await _db.Privacy().CancelAsync(9, bobs)).Outcome);
        Assert.Equal(PrivacyOutcome.NotPending, (await _db.Privacy().ExecuteAsync(bobs, 9, false)).Outcome);
        Assert.Equal("bob", _db.NewContext().Users.Single(u => u.Id == 2).Username);
        Assert.Equal(PrivacyOutcome.NotFound, (await _db.Privacy().ExecuteAsync(999, 9, false)).Outcome);
    }

    [Fact]
    public async Task DueJob_ExecutesOnlyRequestsWithinTheLeadDays_AsTheSystem()
    {
        var aliceId = await RequestFor(1);            // due Now + 30
        _db.Clock = Now.AddDays(10);
        var bobId = await RequestFor(2);              // due Now + 40
        var job = new PrivacyDeletionDueService(_db.Provider(), NullLogger<PrivacyDeletionDueService>.Instance, Options.Create(_db.PrivacyOptions));

        Assert.Equal(0, await job.RunOnceAsync(Now.AddDays(26)));
        _db.Clock = Now.AddDays(27);
        Assert.Equal(1, await job.RunOnceAsync(Now.AddDays(27)));

        var requests = _db.NewContext().PrivacyDeletionRequests.AsNoTracking().ToDictionary(r => r.Id);
        Assert.Equal((PrivacyRequestStatus.Completed, (int?)null), (requests[aliceId].Status, requests[aliceId].ExecutedByUserId));
        Assert.Equal(PrivacyRequestStatus.Pending, requests[bobId].Status);
        _db.Audit.Verify(a => a.RecordAsync(null, 1, AuditAction.PrivacyDeletionExecuted, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task DueJob_DoesNothingWhenSwitchedOff()
    {
        await RequestFor(1);
        _db.PrivacyOptions.AutoExecuteEnabled = false;

        Assert.Equal(0, await _db.Privacy().ExecuteDueAsync(Now.AddDays(60)));
        Assert.Equal(PrivacyRequestStatus.Pending, _db.NewContext().PrivacyDeletionRequests.Single().Status);
    }
}
