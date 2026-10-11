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
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Leaderboards;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    /// <summary>
    /// Leaderboard data access (KNG-34). Snapshot replacement runs in one transaction on MySQL; on
    /// EF InMemory (tests) the same steps run without one. Bulk deletes use ExecuteDelete on MySQL
    /// and tracked removal on InMemory, like StatisticsRepository.
    /// </summary>
    public class LeaderboardRepository : ILeaderboardRepository
    {
        private readonly KnKDbContext _context;

        public LeaderboardRepository(KnKDbContext context)
        {
            _context = context;
        }

        private bool IsRelational => _context.Database.IsRelational();

        // ------------------------------------------------------------------ inputs

        public Task<List<LeaderboardInputRow>> GetTotalsAsync(IReadOnlyCollection<string> metricKeys, CancellationToken ct = default)
        {
            var keys = metricKeys.Distinct().ToList();
            return _context.PlayerStatTotals.AsNoTracking()
                .Where(t => keys.Contains(t.MetricKey) && t.Value > 0)
                .Select(t => new LeaderboardInputRow(t.UserId, t.MetricKey, t.ContextKey, t.Value, t.ReachedAt))
                .ToListAsync(ct);
        }

        public async Task<List<LeaderboardInputRow>> GetDailyAggregatesAsync(IReadOnlyCollection<string> sumMetricKeys,
            IReadOnlyCollection<string> maxMetricKeys, DateOnly from, DateOnly toExclusive, CancellationToken ct = default)
        {
            var sumKeys = sumMetricKeys.Distinct().ToList();
            var maxKeys = maxMetricKeys.Distinct().ToList();
            var inRange = _context.PlayerStatDailies.AsNoTracking().Where(d => d.Day >= from && d.Day < toExclusive && d.Value > 0);

            var sums = await inRange.Where(d => sumKeys.Contains(d.MetricKey))
                .GroupBy(d => new { d.UserId, d.MetricKey, d.ContextKey })
                .Select(g => new { g.Key.UserId, g.Key.MetricKey, g.Key.ContextKey, Value = g.Sum(d => d.Value), At = g.Max(d => d.UpdatedAt) })
                .ToListAsync(ct);
            var result = sums.Select(s => new LeaderboardInputRow(s.UserId, s.MetricKey, s.ContextKey, s.Value, s.At)).ToList();

            // Records (max) are sparse: one row per day a record was set — reduced in memory.
            var records = await inRange.Where(d => maxKeys.Contains(d.MetricKey))
                .Select(d => new { d.UserId, d.MetricKey, d.ContextKey, d.Value, d.UpdatedAt })
                .ToListAsync(ct);
            result.AddRange(records.GroupBy(r => (r.UserId, r.MetricKey, r.ContextKey))
                .Select(g => g.OrderByDescending(r => r.Value).ThenBy(r => r.UpdatedAt).First())
                .Select(r => new LeaderboardInputRow(r.UserId, r.MetricKey, r.ContextKey, r.Value, r.UpdatedAt)));
            return result;
        }

        public async Task<List<(int UserId, int DomainId, DateTime DiscoveredAt)>> GetDiscoveriesAsync(CancellationToken ct = default)
        {
            // Only domains whose discovery type is enabled count, like the player's /discoveries total.
            var enabled = (await DiscoveryEnabledDomains.LoadAsync(new DiscoveryRepository(_context))).Keys.ToHashSet();
            var rows = await _context.UserDomainDiscoveries.AsNoTracking()
                .Select(d => new { d.UserId, d.DomainId, d.DiscoveredAt })
                .ToListAsync(ct);
            return rows.Where(r => enabled.Contains(r.DomainId))
                .Select(r => (r.UserId, r.DomainId, DateTime.SpecifyKind(r.DiscoveredAt, DateTimeKind.Utc))).ToList();
        }

        public async Task<List<(int SecondaryId, int PrimaryId)>> GetMergeLinksAsync(CancellationToken ct = default)
        {
            // Same rule as CurrencyRepository.GetUsersMergedIntoAsync: the forfeit legs of a merge
            // transaction belong to the secondary; SourceRef is the surviving account's id.
            var rows = await _context.CurrencyEntries.AsNoTracking()
                .Where(e => e.Transaction.ReasonCode == CurrencyReasons.MergeForfeit
                    && e.Transaction.SourceType == "User" && e.UserId != null)
                .Select(e => new { Secondary = e.UserId!.Value, e.Transaction.SourceRef })
                .Distinct()
                .ToListAsync(ct);
            var links = new List<(int, int)>();
            foreach (var row in rows)
            {
                if (int.TryParse(row.SourceRef, out var primary) && primary != row.Secondary)
                {
                    links.Add((row.Secondary, primary));
                }
            }
            return links;
        }

        public async Task<Dictionary<int, LeaderboardUserRow>> GetUsersAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
        {
            var ids = userIds.Distinct().ToList();
            var rows = await _context.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new LeaderboardUserRow(u.Id, u.Username, u.IsActive))
                .ToListAsync(ct);
            return rows.ToDictionary(r => r.Id);
        }

        public async Task<Dictionary<int, List<PlayerStatVisibility>>> GetVisibilityAsync(IReadOnlyCollection<string> settingKeys,
            CancellationToken ct = default)
        {
            var keys = settingKeys.Distinct().ToList();
            var rows = await _context.PlayerStatVisibilities.AsNoTracking()
                .Where(v => keys.Contains(v.SettingKey))
                .ToListAsync(ct);
            return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.ToList());
        }

        public async Task<HashSet<int>> GetExcludedUserIdsAsync(CancellationToken ct = default) =>
            (await _context.PlayerStatProfiles.AsNoTracking().Where(p => p.LeaderboardExcluded).Select(p => p.UserId).ToListAsync(ct))
            .ToHashSet();

        // ------------------------------------------------------------------ snapshots

        public async Task ReplaceCurrentAsync(LeaderboardSnapshot snapshot, CancellationToken ct = default)
        {
            if (IsRelational && _context.Database.CurrentTransaction == null)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                await ReplaceAsync(snapshot, ct);
                await transaction.CommitAsync(ct);
            }
            else
            {
                await ReplaceAsync(snapshot, ct);
            }
            _context.ChangeTracker.Clear();
        }

        private async Task ReplaceAsync(LeaderboardSnapshot snapshot, CancellationToken ct)
        {
            var previous = await _context.LeaderboardSnapshots
                .Where(s => s.BoardKey == snapshot.BoardKey && s.Period == snapshot.Period && s.IsCurrent)
                .ToListAsync(ct);
            var superseded = previous.Where(p => p.PeriodStart == snapshot.PeriodStart).Select(p => p.Id).ToList();
            foreach (var closed in previous.Where(p => p.PeriodStart != snapshot.PeriodStart))
            {
                closed.IsCurrent = false; // the final snapshot of a closed week/month, kept until retention
            }
            snapshot.IsCurrent = true;
            _context.LeaderboardSnapshots.Add(snapshot);
            await _context.SaveChangesAsync(ct);
            await DeleteSnapshotsAsync(superseded, ct);
        }

        public async Task<int> PurgeSnapshotsBeforeAsync(DateTime before, CancellationToken ct = default)
        {
            var ids = await _context.LeaderboardSnapshots.AsNoTracking()
                .Where(s => !s.IsCurrent && s.GeneratedAt < before)
                .Select(s => s.Id)
                .ToListAsync(ct);
            await DeleteSnapshotsAsync(ids, ct);
            return ids.Count;
        }

        private async Task DeleteSnapshotsAsync(List<long> ids, CancellationToken ct)
        {
            if (ids.Count == 0) return;
            if (IsRelational)
            {
                await _context.LeaderboardSnapshotEntries.Where(e => ids.Contains(e.SnapshotId)).ExecuteDeleteAsync(ct);
                await _context.LeaderboardSnapshots.Where(s => ids.Contains(s.Id)).ExecuteDeleteAsync(ct);
                return;
            }
            // EF InMemory has no ExecuteDelete.
            _context.LeaderboardSnapshotEntries.RemoveRange(
                await _context.LeaderboardSnapshotEntries.Where(e => ids.Contains(e.SnapshotId)).ToListAsync(ct));
            _context.LeaderboardSnapshots.RemoveRange(await _context.LeaderboardSnapshots.Where(s => ids.Contains(s.Id)).ToListAsync(ct));
            await _context.SaveChangesAsync(ct);
        }

        public Task<LeaderboardSnapshot?> GetCurrentAsync(string boardKey, LeaderboardPeriod period, CancellationToken ct = default) =>
            _context.LeaderboardSnapshots.AsNoTracking()
                .Where(s => s.BoardKey == boardKey && s.Period == period && s.IsCurrent)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(ct);

        public Task<List<LeaderboardSnapshotEntry>> GetTopEntriesAsync(long snapshotId, int top, CancellationToken ct = default) =>
            _context.LeaderboardSnapshotEntries.AsNoTracking()
                .Where(e => e.SnapshotId == snapshotId)
                .OrderBy(e => e.Rank).ThenBy(e => e.ReachedAt).ThenBy(e => e.UserId)
                .Take(top)
                .ToListAsync(ct);

        public Task<LeaderboardSnapshotEntry?> GetEntryAsync(long snapshotId, int userId, CancellationToken ct = default) =>
            _context.LeaderboardSnapshotEntries.AsNoTracking()
                .FirstOrDefaultAsync(e => e.SnapshotId == snapshotId && e.UserId == userId, ct);

        // ------------------------------------------------------------------ exclusions

        public Task<List<PlayerStatProfile>> GetExclusionsAsync(CancellationToken ct = default) =>
            _context.PlayerStatProfiles.AsNoTracking()
                .Where(p => p.LeaderboardExcluded)
                .OrderBy(p => p.UserId)
                .ToListAsync(ct);

        public Task<PlayerStatProfile?> GetProfileForUpdateAsync(int userId, CancellationToken ct = default) =>
            _context.PlayerStatProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);

        public void AddProfile(PlayerStatProfile profile) => _context.PlayerStatProfiles.Add(profile);

        public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
    }
}
