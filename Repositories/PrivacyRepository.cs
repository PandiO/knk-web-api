using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    /// <summary>
    /// GDPR deletion data access (KNG-34 link 6, DESIGN.md §F.14 + link 5's additions + developer
    /// decisions 2026-10-03). The erasure scope is listed once in <see cref="Scopes"/> so counting
    /// (dry run) and deleting can never disagree. Ledger and Siege match rows are never touched here.
    /// Road-builder proposals keep their road data; only the requesting player's name is cleared.
    /// </summary>
    public class PrivacyRepository : IPrivacyRepository
    {
        private readonly KnKDbContext _context;

        public PrivacyRepository(KnKDbContext context)
        {
            _context = context;
        }

        private bool IsRelational => _context.Database.IsRelational();

        /// <summary>Erasure scope: table name → rows of the given users.</summary>
        private IEnumerable<(string Table, Func<IReadOnlyCollection<int>, CancellationToken, Task<int>> Count,
            Func<IReadOnlyCollection<int>, CancellationToken, Task<int>> Delete)> Scopes()
        {
            yield return Scope("player_stat_daily", ids => _context.PlayerStatDailies.Where(r => ids.Contains(r.UserId)));
            yield return Scope("player_stat_totals", ids => _context.PlayerStatTotals.Where(r => ids.Contains(r.UserId)));
            yield return Scope("player_stat_sessions", ids => _context.PlayerStatSessions.Where(r => ids.Contains(r.UserId)));
            yield return Scope("player_stat_visibility", ids => _context.PlayerStatVisibilities.Where(r => ids.Contains(r.UserId)));
            // The profile row also holds the leaderboard exclusion columns (link 5).
            yield return Scope("player_stat_profiles", ids => _context.PlayerStatProfiles.Where(r => ids.Contains(r.UserId)));
            yield return Scope("player_title_changes", ids => _context.PlayerTitleChanges.Where(r => ids.Contains(r.UserId)));
            yield return Scope("player_pvp_kill_pairs_daily", ids => _context.PlayerPvpKillPairDailies
                .Where(r => ids.Contains(r.KillerUserId) || ids.Contains(r.VictimUserId)));
            yield return Scope("leaderboard_snapshot_entries", ids => _context.LeaderboardSnapshotEntries.Where(r => ids.Contains(r.UserId)));
            yield return Scope("telemetry_events", ids => _context.TelemetryEvents.Where(r => r.UserId != null && ids.Contains(r.UserId.Value)));
            yield return Scope("telemetry_enhanced_targets", ids => _context.TelemetryEnhancedTargets
                .Where(r => r.UserId != null && ids.Contains(r.UserId.Value)));
            yield return Scope("user_domain_discoveries", ids => _context.UserDomainDiscoveries.Where(r => ids.Contains(r.UserId)));
            // Developer decision 2026-10-03: "delete means everything" beyond statistics.
            yield return Scope("private_message_logs", ids => _context.PrivateMessageLogEntries
                .Where(r => (r.SenderUserId != null && ids.Contains(r.SenderUserId.Value))
                            || (r.RecipientUserId != null && ids.Contains(r.RecipientUserId.Value))));
            yield return Scope("link_codes", ids => _context.LinkCodes.Where(r => r.UserId != null && ids.Contains(r.UserId.Value)));
            yield return Scope("permission_grants", ids => _context.PermissionGrants.Where(r => ids.Contains(r.HolderId)));
            yield return Scope("user_permission_groups", ids => _context.UserPermissionGroups.Where(r => ids.Contains(r.UserId)));
            // Rows about the player (target). Rows the player wrote as staff about others stay: they
            // are those players' moderation history and name only the pseudonymized id.
            yield return Scope("audit_log_entries", ids => _context.AuditLogEntries.Where(r => ids.Contains(r.TargetUserId)));
        }

        private (string, Func<IReadOnlyCollection<int>, CancellationToken, Task<int>>, Func<IReadOnlyCollection<int>, CancellationToken, Task<int>>)
            Scope<T>(string table, Func<List<int>, IQueryable<T>> query) where T : class =>
            (table,
             (ids, ct) => query(ids.ToList()).CountAsync(ct),
             (ids, ct) => DeleteAsync(query(ids.ToList()), ct));

        public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
        {
            if (!IsRelational || _context.Database.CurrentTransaction != null)
            {
                return await work();
            }
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var result = await work();
            await transaction.CommitAsync(ct);
            return result;
        }

        public Task<List<PrivacyDeletionRequest>> GetRequestsAsync(PrivacyRequestStatus? status, CancellationToken ct = default)
        {
            var query = _context.PrivacyDeletionRequests.AsNoTracking().AsQueryable();
            if (status != null) query = query.Where(r => r.Status == status);
            return query.OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.Id).ToListAsync(ct);
        }

        public Task<PrivacyDeletionRequest?> GetRequestAsync(int id, CancellationToken ct = default) =>
            _context.PrivacyDeletionRequests.FirstOrDefaultAsync(r => r.Id == id, ct);

        public Task<PrivacyDeletionRequest?> GetOpenRequestOfUserAsync(int userId, CancellationToken ct = default) =>
            _context.PrivacyDeletionRequests
                .Where(r => r.UserId == userId
                            && (r.Status == PrivacyRequestStatus.Pending || r.Status == PrivacyRequestStatus.AwaitingConfirmation))
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync(ct);

        public Task<PrivacyDeletionRequest?> GetRequestByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
            _context.PrivacyDeletionRequests.FirstOrDefaultAsync(r => r.ConfirmationTokenHash == tokenHash, ct);

        public Task<List<PrivacyDeletionRequest>> GetScheduledDueAsync(DateTime now, CancellationToken ct = default) =>
            _context.PrivacyDeletionRequests.AsNoTracking()
                .Where(r => r.Status == PrivacyRequestStatus.Pending && r.ScheduledAt != null && r.ScheduledAt <= now)
                .OrderBy(r => r.ScheduledAt)
                .ThenBy(r => r.Id)
                .ToListAsync(ct);

        public Task<List<PrivacyDeletionRequest>> GetExpiredConfirmationsAsync(DateTime now, CancellationToken ct = default) =>
            _context.PrivacyDeletionRequests
                .Where(r => r.Status == PrivacyRequestStatus.AwaitingConfirmation
                            && (r.ConfirmationExpiresAt == null || r.ConfirmationExpiresAt <= now))
                .ToListAsync(ct);

        public async Task AddRequestAsync(PrivacyDeletionRequest request, CancellationToken ct = default)
        {
            _context.PrivacyDeletionRequests.Add(request);
            await _context.SaveChangesAsync(ct);
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

        public Task<User?> GetUserAsync(int userId, CancellationToken ct = default) =>
            _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

        public async Task<Dictionary<int, string>> GetUsernamesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
        {
            if (userIds.Count == 0) return new Dictionary<int, string>();
            var ids = userIds.Distinct().ToList();
            return await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        }

        public async Task<SortedDictionary<string, int>> CountUserDataAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var (table, count, _) in Scopes())
            {
                counts[table] = await count(userIds, ct);
            }
            var names = await UsernamesOfAsync(userIds, ct);
            counts[RoadProposalNamesKey] = await RoadProposalsNaming(names).CountAsync(ct);
            return counts;
        }

        public async Task<SortedDictionary<string, int>> EraseUserDataAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var (table, _, delete) in Scopes())
            {
                counts[table] = await delete(userIds, ct);
            }
            // Runs before PseudonymizeUsersAsync, while the usernames are still known.
            counts[RoadProposalNamesKey] = await ClearRoadProposalNamesAsync(await UsernamesOfAsync(userIds, ct), ct);
            return counts;
        }

        public async Task<int> PseudonymizeUsersAsync(IReadOnlyCollection<int> userIds, DateTime now, string reason, CancellationToken ct = default)
        {
            var ids = userIds.ToList();
            var users = await _context.Users.Where(u => ids.Contains(u.Id)).ToListAsync(ct);
            foreach (var user in users)
            {
                user.Username = PseudonymOf(user.Id);
                user.Email = null;
                user.EmailVerified = false;
                user.Uuid = null;
                user.PasswordHash = null;
                user.Gender = null;
                user.ChatPrefix = null;
                user.ChatSuffix = null;
                user.IsOnline = false;
                user.LastSeenAt = null;
                user.IsActive = false;
                user.DeletedAt ??= now;
                user.DeletedReason = reason;
            }
            await _context.SaveChangesAsync(ct);
            return users.Count;
        }

        /// <summary>Result key for the road-builder proposals whose requester name was cleared.</summary>
        public const string RoadProposalNamesKey = "road_tile_proposals.created_by";

        private async Task<List<string>> UsernamesOfAsync(IReadOnlyCollection<int> userIds, CancellationToken ct)
        {
            var ids = userIds.ToList();
            return await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => u.Username).ToListAsync(ct);
        }

        /// <summary>Road-builder proposals (KNG-27) store the requesting player's name as plain text.</summary>
        private IQueryable<RoadTileProposal> RoadProposalsNaming(List<string> names) =>
            _context.RoadTileProposals.Where(p => p.CreatedBy != null && names.Contains(p.CreatedBy));

        /// <summary>Clears the player's name from road-builder proposals (developer decision 2026-10-10); the
        /// proposals themselves are road data and stay.</summary>
        private async Task<int> ClearRoadProposalNamesAsync(List<string> names, CancellationToken ct)
        {
            if (names.Count == 0) return 0;
            if (IsRelational)
            {
                return await RoadProposalsNaming(names).ExecuteUpdateAsync(u => u.SetProperty(p => p.CreatedBy, (string?)null), ct);
            }
            var rows = await RoadProposalsNaming(names).ToListAsync(ct);
            rows.ForEach(p => p.CreatedBy = null);
            await _context.SaveChangesAsync(ct);
            return rows.Count;
        }

        /// <summary>The username a pseudonymized account gets.</summary>
        public static string PseudonymOf(int userId) => $"deleted-{userId}";

        private async Task<int> DeleteAsync<T>(IQueryable<T> query, CancellationToken ct) where T : class
        {
            if (IsRelational)
            {
                return await query.ExecuteDeleteAsync(ct);
            }
            // EF InMemory has no ExecuteDelete.
            var rows = await query.ToListAsync(ct);
            _context.RemoveRange(rows);
            await _context.SaveChangesAsync(ct);
            return rows.Count;
        }
    }
}
