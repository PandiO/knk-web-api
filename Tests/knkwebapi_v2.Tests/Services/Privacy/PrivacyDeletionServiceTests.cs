using System.Text.Json;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Privacy;
using knkwebapi_v2.Tests.Services.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Privacy;

/// <summary>
/// GDPR erasure (DESIGN.md §F.14 + link 5's additions; link 6 acceptance criterion 4; developer
/// decisions 2026-10-03): the player's request is confirmed by an emailed link, staff file without
/// it, a 5-day grace period allows cancelling; dry run; execution deletes exactly the scope per
/// table (the player's and merged accounts' rows incl. PM logs, link codes, grants, group
/// memberships and audit rows about them; other players' rows stay; kill pairs as killer or victim),
/// pseudonymizes the account, keeps the ledger and Siege rows, is idempotent; the hourly job only
/// acts on confirmed requests after their grace period and respects its switch.
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
        // Personal data beyond statistics (developer decision 2026-10-03).
        c.PrivateMessageLogEntries.AddRange(
            new PrivateMessageLogEntry { SentAt = Now, ClientMessageId = Guid.NewGuid(), SenderUserId = 1, SenderName = "alice", RecipientUserId = 2, RecipientName = "bob", Content = "hi" },
            new PrivateMessageLogEntry { SentAt = Now, ClientMessageId = Guid.NewGuid(), SenderUserId = 2, SenderName = "bob", RecipientUserId = 3, RecipientName = "carol", Content = "yo" },
            new PrivateMessageLogEntry { SentAt = Now, ClientMessageId = Guid.NewGuid(), SenderUserId = 2, SenderName = "bob", RecipientUserId = null, RecipientName = "console", Content = "ok" });
        c.LinkCodes.AddRange(new LinkCode { UserId = 1, Code = "AAAA1111", ExpiresAt = Now.AddHours(1) },
            new LinkCode { UserId = 2, Code = "BBBB2222", ExpiresAt = Now.AddHours(1) });
        c.PermissionGroups.Add(new PermissionGroup { Id = 50, Name = "Knight", Weight = 1 });
        c.PermissionGrants.AddRange(new PermissionGrant { HolderId = 1, Node = "knk.a" }, new PermissionGrant { HolderId = 2, Node = "knk.b" },
            new PermissionGrant { HolderId = 50, Node = "knk.group" });
        c.UserPermissionGroups.AddRange(new UserPermissionGroup { UserId = 1, PermissionGroupId = 50 },
            new UserPermissionGroup { UserId = 2, PermissionGroupId = 50 });
        // Road-builder proposals name who asked for the build (KNG-27): alice's name is cleared, bob's stays.
        c.RoadTiles.Add(new RoadTile { Id = 1, World = "world" });
        c.RoadTileProposals.AddRange(new RoadTileProposal { TileId = 1, CreatedBy = "alice" },
            new RoadTileProposal { TileId = 1, CreatedBy = "bob" }, new RoadTileProposal { TileId = 1, CreatedBy = null });
        c.AuditLogEntries.AddRange(new AuditLogEntry { ActorUserId = 2, TargetUserId = 1, Action = AuditAction.TitleChanged },
            new AuditLogEntry { ActorUserId = 1, TargetUserId = 2, Action = AuditAction.TitleChanged },
            new AuditLogEntry { ActorUserId = null, TargetUserId = 3, Action = AuditAction.TitleChanged });
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

    /// <summary>Staff files for <paramref name="userId"/> (no email step) and the grace period passes.</summary>
    private async Task<int> ScheduledFor(int userId = 1)
    {
        var result = await _db.Privacy().FileForPlayerAsync(9, PrivacyRequestSource.Staff, userId, " from email ");
        Assert.Equal(PrivacyOutcome.Ok, result.Outcome);
        _db.Clock = Now.AddDays(5);
        return result.Request!.Id;
    }

    private string? _lastToken;

    private void CaptureConfirmationLinks() =>
        _db.Email.Setup(e => e.SendConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .Callback<string, string, string, DateTime>((_, _, url, _) => _lastToken = Uri.UnescapeDataString(url[(url.IndexOf("token=", StringComparison.Ordinal) + 6)..]))
            .Returns(Task.CompletedTask);

    // ------------------------------------------------------------------ the player's own request

    [Fact]
    public async Task PlayerRequest_EmailsALink_ConfirmingSchedulesItAfterTheGracePeriod()
    {
        CaptureConfirmationLinks();

        var requested = await _db.Privacy().RequestOwnAsync(1);

        Assert.Equal(PrivacyOutcome.Ok, requested.Outcome);
        Assert.Equal((PrivacyRequestStatus.AwaitingConfirmation, PrivacyRequestSource.Player, (DateTime?)Now.AddHours(24), (DateTime?)null),
            (requested.Request!.Status, requested.Request.Source, requested.Request.ConfirmationExpiresAt, requested.Request.ScheduledAt));
        _db.Email.Verify(e => e.SendConfirmationAsync("a@example.org", "alice",
            It.Is<string>(u => u.StartsWith("https://knk.example/account/delete-data/confirm?token=")), Now.AddHours(24)), Times.Once);
        Assert.NotNull(_lastToken);
        // Only the hash is stored.
        Assert.NotEqual(_lastToken, _db.NewContext().PrivacyDeletionRequests.Single().ConfirmationTokenHash);

        _db.Clock = Now.AddHours(2);
        var confirmed = await _db.Privacy().ConfirmAsync(_lastToken!);

        Assert.Equal(PrivacyOutcome.Ok, confirmed.Outcome);
        Assert.Equal((PrivacyRequestStatus.Pending, (DateTime?)Now.AddHours(2).AddDays(5), Now.AddHours(2).AddDays(30)),
            (confirmed.Request!.Status, confirmed.Request.ScheduledAt, confirmed.Request.DueAt));
        _db.Email.Verify(e => e.SendScheduledAsync("a@example.org", "alice", Now.AddHours(2).AddDays(5), "https://knk.example/account", false), Times.Once);
        _db.Audit.Verify(a => a.RecordAsync(1, 1, AuditAction.PrivacyDeletionConfirmed, It.IsAny<string>()), Times.Once);
        // A link works once.
        Assert.Equal(PrivacyOutcome.InvalidToken, (await _db.Privacy().ConfirmAsync(_lastToken!)).Outcome);
        Assert.Equal(PrivacyOutcome.InvalidToken, (await _db.Privacy().ConfirmAsync("nope")).Outcome);
        // Asking again while scheduled returns the scheduled request.
        var again = await _db.Privacy().RequestOwnAsync(1);
        Assert.Equal((PrivacyOutcome.AlreadyPending, confirmed.Request.Id), (again.Outcome, again.Request!.Id));
    }

    [Fact]
    public async Task PlayerRequest_NeedsAnEmailAddress_AndALiveAccount()
    {
        Assert.Equal(PrivacyOutcome.EmailRequired, (await _db.Privacy().RequestOwnAsync(2)).Outcome);
        Assert.Equal(PrivacyOutcome.UserNotFound, (await _db.Privacy().RequestOwnAsync(99)).Outcome);
        Assert.Empty(_db.NewContext().PrivacyDeletionRequests);
    }

    [Fact]
    public async Task PlayerRequest_AgainSendsAFreshLink_AfterTheCooldown_AndTheOldLinkStopsWorking()
    {
        CaptureConfirmationLinks();
        await _db.Privacy().RequestOwnAsync(1);
        var first = _lastToken;

        await _db.Privacy().RequestOwnAsync(1); // within the cooldown: no second email
        _db.Email.Verify(e => e.SendConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Once);

        _db.Cache = new MemoryCache(new MemoryCacheOptions()); // the cooldown has passed
        await _db.Privacy().RequestOwnAsync(1);

        Assert.NotEqual(first, _lastToken);
        Assert.Single(_db.NewContext().PrivacyDeletionRequests);
        Assert.Equal(PrivacyOutcome.InvalidToken, (await _db.Privacy().ConfirmAsync(first!)).Outcome);
        Assert.Equal(PrivacyOutcome.Ok, (await _db.Privacy().ConfirmAsync(_lastToken!)).Outcome);
    }

    [Fact]
    public async Task PlayerRequest_UnconfirmedLinkExpires_AndNothingHappens()
    {
        CaptureConfirmationLinks();
        await _db.Privacy().RequestOwnAsync(1);
        _db.Clock = Now.AddHours(25);

        Assert.Equal(PrivacyOutcome.InvalidToken, (await _db.Privacy().ConfirmAsync(_lastToken!)).Outcome);
        Assert.Equal(1, await _db.Privacy().ExpireConfirmationsAsync(_db.Clock));
        Assert.Equal(0, await _db.Privacy().ExecuteScheduledAsync(Now.AddDays(60)));

        Assert.Equal(PrivacyRequestStatus.Expired, _db.NewContext().PrivacyDeletionRequests.Single().Status);
        Assert.Equal("alice", _db.NewContext().Users.Single(u => u.Id == 1).Username);
    }

    [Fact]
    public async Task PlayerRequest_EmailFailure_KeepsItUnconfirmed()
    {
        _db.Email.Setup(e => e.SendConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var result = await _db.Privacy().RequestOwnAsync(1);

        Assert.Equal((PrivacyOutcome.EmailFailed, PrivacyRequestStatus.AwaitingConfirmation), (result.Outcome, result.Request!.Status));
    }

    [Fact]
    public async Task Player_CanCancel_DuringTheGracePeriod_AndTheJobLeavesItAlone()
    {
        CaptureConfirmationLinks();
        await _db.Privacy().RequestOwnAsync(1);
        await _db.Privacy().ConfirmAsync(_lastToken!);
        _db.Clock = Now.AddDays(4);

        var cancelled = await _db.Privacy().CancelOwnAsync(1);

        Assert.Equal((PrivacyRequestStatus.Cancelled, (int?)1, (DateTime?)Now.AddDays(4)),
            (cancelled.Request!.Status, cancelled.Request.CancelledByUserId, cancelled.Request.CancelledAt));
        Assert.Equal(0, await _db.Privacy().ExecuteScheduledAsync(Now.AddDays(6)));
        Assert.Equal("alice", _db.NewContext().Users.Single(u => u.Id == 1).Username);
        Assert.Equal(PrivacyOutcome.NotFound, (await _db.Privacy().CancelOwnAsync(1)).Outcome);
    }

    // ------------------------------------------------------------------ staff / owner filing

    [Fact]
    public async Task StaffFiling_SkipsTheEmailConfirmation_ButKeepsTheGracePeriod()
    {
        var filed = await _db.Privacy().FileForPlayerAsync(9, PrivacyRequestSource.Staff, 1, " ticket 12 ");

        Assert.Equal(PrivacyOutcome.Ok, filed.Outcome);
        Assert.Equal((PrivacyRequestStatus.Pending, PrivacyRequestSource.Staff, (DateTime?)Now.AddDays(5), Now.AddDays(30), "ticket 12", true),
            (filed.Request!.Status, filed.Request.Source, filed.Request.ScheduledAt, filed.Request.DueAt, filed.Request.Note, filed.Request.AutoExecute));
        _db.Email.Verify(e => e.SendConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        _db.Email.Verify(e => e.SendScheduledAsync("a@example.org", "alice", Now.AddDays(5), "https://knk.example/account", true), Times.Once);
        _db.Audit.Verify(a => a.RecordAsync(9, 1, AuditAction.PrivacyDeletionRequested, It.IsAny<string>()), Times.Once);

        Assert.Equal(PrivacyOutcome.GracePeriod, (await _db.Privacy().ExecuteAsync(filed.Request.Id, 9, dryRun: false)).Outcome);
        Assert.Equal(PrivacyOutcome.AlreadyPending, (await _db.Privacy().FileForPlayerAsync(9, PrivacyRequestSource.Staff, 1, null)).Outcome);
        Assert.Equal(PrivacyOutcome.UserNotFound, (await _db.Privacy().FileForPlayerAsync(9, PrivacyRequestSource.Staff, 99, null)).Outcome);
        // A player without an email address is not notified; the request stands.
        Assert.Equal(PrivacyOutcome.Ok, (await _db.Privacy().FileForPlayerAsync(9, PrivacyRequestSource.Staff, 2, null)).Outcome);
        _db.Email.Verify(e => e.SendScheduledAsync(It.IsAny<string>(), "bob", It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task StaffFiling_TakesOverAnUnconfirmedPlayerRequest()
    {
        CaptureConfirmationLinks();
        var own = await _db.Privacy().RequestOwnAsync(1);

        var filed = await _db.Privacy().FileForPlayerAsync(9, PrivacyRequestSource.Staff, 1, null);

        Assert.Equal((own.Request!.Id, PrivacyRequestStatus.Pending, PrivacyRequestSource.Staff), (filed.Request!.Id, filed.Request.Status, filed.Request.Source));
        Assert.Equal(PrivacyOutcome.InvalidToken, (await _db.Privacy().ConfirmAsync(_lastToken!)).Outcome);
    }

    // ------------------------------------------------------------------ execution

    [Fact]
    public async Task DryRun_CountsTheScope_AndChangesNothing()
    {
        var filed = await _db.Privacy().FileForPlayerAsync(9, PrivacyRequestSource.Staff, 1, null);

        var preview = await _db.Privacy().ExecuteAsync(filed.Request!.Id, 9, dryRun: true); // allowed during the grace period

        var result = preview.Request!.Result!;
        Assert.True(result.DryRun);
        Assert.Equal(new[] { 1, 3 }, result.UserIds);
        Assert.Equal(4, result.Deleted["player_stat_daily"]);
        Assert.Equal(3, result.Deleted["player_pvp_kill_pairs_daily"]);
        Assert.Equal((2, 1, 1, 1, 2), (result.Deleted["private_message_logs"], result.Deleted["link_codes"], result.Deleted["permission_grants"],
            result.Deleted["user_permission_groups"], result.Deleted["audit_log_entries"]));
        Assert.Equal(1, result.Deleted["road_tile_proposals.created_by"]);
        Assert.Equal(PrivacyRequestStatus.Pending, preview.Request.Status);
        Assert.Equal((6, 3, 3), (_db.NewContext().PlayerStatDailies.Count(), _db.NewContext().PlayerStatTotals.Count(), _db.NewContext().TelemetryEvents.Count(e => e.UserId != null)));
        Assert.Equal("alice", _db.NewContext().Users.Single(u => u.Id == 1).Username);
    }

    [Fact]
    public async Task Execute_DeletesExactlyTheScope_PerTable()
    {
        var id = await ScheduledFor();

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
        // Beyond statistics: PMs to/from them, link codes, grants, memberships, audit rows about them.
        Assert.Equal("ok", Assert.Single(c.PrivateMessageLogEntries).Content);
        Assert.Equal(2, Assert.Single(c.LinkCodes).UserId);
        Assert.Equal(new[] { 2, 50 }, c.PermissionGrants.Select(g => g.HolderId).OrderBy(h => h).ToArray());
        Assert.Equal(2, Assert.Single(c.UserPermissionGroups).UserId);
        var audit = Assert.Single(c.AuditLogEntries); // the row alice wrote about bob stays (bob's history)
        Assert.Equal((2, (int?)1), (audit.TargetUserId, audit.ActorUserId));
        // Road proposals stay (road data); only alice's name is gone.
        Assert.Equal(new string?[] { null, null, "bob" }, c.RoadTileProposals.Select(p => p.CreatedBy).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task Execute_PseudonymizesTheAccounts_AndKeepsLedgerAndSiege()
    {
        var id = await ScheduledFor();

        var executed = await _db.Privacy().ExecuteAsync(id, 9, dryRun: false);

        var c = _db.NewContext();
        var alice = c.Users.Single(u => u.Id == 1);
        Assert.Equal(("deleted-1", (string?)null, (string?)null, (string?)null, false), (alice.Username, alice.Email, alice.Uuid, alice.PasswordHash, alice.IsActive));
        Assert.Equal((Now.AddDays(5), "GDPR erasure", (Gender?)null), (alice.DeletedAt, alice.DeletedReason, alice.Gender));
        Assert.Equal("deleted-3", c.Users.Single(u => u.Id == 3).Username);
        Assert.Equal("bob", c.Users.Single(u => u.Id == 2).Username);
        Assert.Equal(2, c.CurrencyTransactions.Count());
        Assert.Equal(1, c.SiegeMatchParticipants.Count(p => p.UserId == 1));
        Assert.Equal((2, "deleted-1", (string?)null), (executed.Request!.Result!.PseudonymizedUsers, executed.Request.Username, executed.Request.Note));
        _db.Audit.Verify(a => a.RecordAsync(9, 1, AuditAction.PrivacyDeletionExecuted, It.IsAny<string>()), Times.Once);
        // An erased account can't request or be filed again.
        Assert.Equal(PrivacyOutcome.UserNotFound, (await _db.Privacy().FileForPlayerAsync(9, PrivacyRequestSource.Staff, 1, null)).Outcome);
    }

    [Fact]
    public async Task Execute_IsIdempotent_AndCancelledRequestsAreRefused()
    {
        var id = await ScheduledFor();
        var first = await _db.Privacy().ExecuteAsync(id, 9, dryRun: false);
        _db.Clock = Now.AddDays(6);

        var second = await _db.Privacy().ExecuteAsync(id, 9, dryRun: false);

        Assert.Equal(PrivacyOutcome.Ok, second.Outcome);
        Assert.Equal(first.Request!.ExecutedAt, second.Request!.ExecutedAt);
        Assert.Equal(JsonSerializer.Serialize(first.Request.Result), JsonSerializer.Serialize(second.Request.Result));
        _db.Audit.Verify(a => a.RecordAsync(9, 1, AuditAction.PrivacyDeletionExecuted, It.IsAny<string>()), Times.Once);
        Assert.Equal(PrivacyOutcome.NotPending, (await _db.Privacy().CancelAsync(9, id)).Outcome);

        var bobs = (await _db.Privacy().FileForPlayerAsync(9, PrivacyRequestSource.Staff, 2, null)).Request!.Id;
        Assert.Equal(PrivacyRequestStatus.Cancelled, (await _db.Privacy().CancelAsync(9, bobs)).Request!.Status);
        Assert.Equal(PrivacyOutcome.Ok, (await _db.Privacy().CancelAsync(9, bobs)).Outcome);
        _db.Clock = Now.AddDays(20);
        Assert.Equal(PrivacyOutcome.NotPending, (await _db.Privacy().ExecuteAsync(bobs, 9, false)).Outcome);
        Assert.Equal("bob", _db.NewContext().Users.Single(u => u.Id == 2).Username);
        Assert.Equal(PrivacyOutcome.NotFound, (await _db.Privacy().ExecuteAsync(999, 9, false)).Outcome);
    }

    [Fact]
    public async Task Job_ExecutesOnlyAfterTheGracePeriod_AsTheSystem()
    {
        var alice = (await _db.Privacy().FileForPlayerAsync(9, PrivacyRequestSource.Staff, 1, null)).Request!.Id; // runs Now + 5
        _db.Clock = Now.AddDays(2);
        var bob = (await _db.Privacy().FileForPlayerAsync(9, PrivacyRequestSource.Staff, 2, null)).Request!.Id;   // runs Now + 7
        var job = new PrivacyDeletionDueService(_db.Provider(), NullLogger<PrivacyDeletionDueService>.Instance, Options.Create(_db.PrivacyOptions));

        _db.Clock = Now.AddDays(5).AddMinutes(-1);
        Assert.Equal(0, await job.RunOnceAsync(_db.Clock));
        _db.Clock = Now.AddDays(5);
        Assert.Equal(1, await job.RunOnceAsync(_db.Clock));

        var requests = _db.NewContext().PrivacyDeletionRequests.AsNoTracking().ToDictionary(r => r.Id);
        Assert.Equal((PrivacyRequestStatus.Completed, (int?)null), (requests[alice].Status, requests[alice].ExecutedByUserId));
        Assert.Equal(PrivacyRequestStatus.Pending, requests[bob].Status);
        _db.Audit.Verify(a => a.RecordAsync(null, 1, AuditAction.PrivacyDeletionExecuted, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Job_DoesNotExecuteWhenSwitchedOff_ButStillExpiresLinks()
    {
        CaptureConfirmationLinks();
        await _db.Privacy().FileForPlayerAsync(9, PrivacyRequestSource.Staff, 1, null);
        _db.PrivacyOptions.AutoExecuteEnabled = false;

        Assert.Equal(0, await _db.Privacy().ExecuteScheduledAsync(Now.AddDays(60)));
        Assert.Equal(PrivacyRequestStatus.Pending, _db.NewContext().PrivacyDeletionRequests.Single().Status);
        Assert.False((await _db.Privacy().GetOpenRequestOfUserAsync(1)).Request!.AutoExecute);
    }
}
