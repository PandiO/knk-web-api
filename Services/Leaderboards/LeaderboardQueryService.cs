using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Statistics;

namespace knkwebapi_v2.Services.Leaderboards
{
    /// <summary>
    /// Leaderboard reads and owner exclusions (KNG-34, IMPLEMENTATION_PLAN.md §3.2). Reads use the
    /// current snapshot only (never raw history, DESIGN.md §F.11); values are display-rounded like
    /// the statistics reads. Exclusions live on player_stat_profiles and apply from the next refresh.
    /// </summary>
    public class LeaderboardQueryService : ILeaderboardQueryService
    {
        public const int MaxReasonLength = 200;

        private readonly ILeaderboardRepository _repo;
        private readonly TimeProvider _time;

        public LeaderboardQueryService(ILeaderboardRepository repo, TimeProvider? time = null)
        {
            _repo = repo;
            _time = time ?? TimeProvider.System;
        }

        public IReadOnlyList<LeaderboardBoardDto> GetBoards() => LeaderboardCatalog.Boards.Select(b => new LeaderboardBoardDto
        {
            BoardKey = b.BoardKey,
            Metric = b.Metric.Key,
            Context = b.Context,
            Label = b.Label,
            Unit = b.Unit,
            Periods = LeaderboardCatalog.Periods.Select(LeaderboardCatalog.PeriodName).ToList(),
            AlwaysPublic = b.AlwaysPublic
        }).ToList();

        public async Task<LeaderboardViewDto> GetBoardAsync(LeaderboardBoardDefinition board, LeaderboardPeriod period, int top,
            int? viewerUserId, CancellationToken ct = default)
        {
            var view = new LeaderboardViewDto
            {
                BoardKey = board.BoardKey,
                Label = board.Label,
                Unit = board.Unit,
                Period = LeaderboardCatalog.PeriodName(period)
            };
            var snapshot = await _repo.GetCurrentAsync(board.BoardKey, period, ct);
            if (snapshot == null) return view;

            view.PeriodStart = snapshot.PeriodStart;
            view.GeneratedAt = DateTime.SpecifyKind(snapshot.GeneratedAt, DateTimeKind.Utc);
            view.TotalRanked = snapshot.EntryCount;

            var entries = await _repo.GetTopEntriesAsync(snapshot.Id, top, ct);
            var names = await _repo.GetUsersAsync(entries.Select(e => e.UserId).ToList(), ct);
            view.Entries = entries.Select(e => new LeaderboardRankEntryDto
            {
                Rank = e.Rank,
                UserId = e.UserId,
                Username = names.TryGetValue(e.UserId, out var user) ? user.Username : $"#{e.UserId}",
                Value = StatisticsFormatting.Display(board.Metric, e.Value),
                RawValue = e.Value
            }).ToList();

            if (viewerUserId != null)
            {
                var own = await _repo.GetEntryAsync(snapshot.Id, viewerUserId.Value, ct);
                if (own != null)
                {
                    view.Viewer = new LeaderboardViewerEntryDto
                    {
                        Rank = own.Rank,
                        Value = StatisticsFormatting.Display(board.Metric, own.Value),
                        RawValue = own.Value
                    };
                }
            }
            return view;
        }

        public async Task<List<LeaderboardExclusionDto>> GetExclusionsAsync(CancellationToken ct = default)
        {
            var profiles = await _repo.GetExclusionsAsync(ct);
            var names = await _repo.GetUsersAsync(profiles.Select(p => p.UserId).ToList(), ct);
            return profiles.Select(p => new LeaderboardExclusionDto
            {
                UserId = p.UserId,
                Username = names.TryGetValue(p.UserId, out var user) ? user.Username : null,
                Reason = p.LeaderboardExcludedReason,
                ExcludedByUserId = p.LeaderboardExcludedByUserId,
                UpdatedAt = DateTime.SpecifyKind(p.UpdatedAt, DateTimeKind.Utc)
            }).ToList();
        }

        public async Task<bool> ExcludeAsync(int userId, string? reason, int byUserId, CancellationToken ct = default)
        {
            if (!(await _repo.GetUsersAsync(new[] { userId }, ct)).ContainsKey(userId)) return false;
            var text = reason?.Trim();
            if (text is { Length: > MaxReasonLength }) text = text[..MaxReasonLength];
            var profile = await GetOrAddProfileAsync(userId, ct);
            profile.LeaderboardExcluded = true;
            profile.LeaderboardExcludedReason = string.IsNullOrEmpty(text) ? null : text;
            profile.LeaderboardExcludedByUserId = byUserId;
            profile.UpdatedAt = _time.GetUtcNow().UtcDateTime;
            await _repo.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> IncludeAsync(int userId, CancellationToken ct = default)
        {
            if (!(await _repo.GetUsersAsync(new[] { userId }, ct)).ContainsKey(userId)) return false;
            var profile = await _repo.GetProfileForUpdateAsync(userId, ct);
            if (profile == null || !profile.LeaderboardExcluded) return true;
            profile.LeaderboardExcluded = false;
            profile.LeaderboardExcludedReason = null;
            profile.LeaderboardExcludedByUserId = null;
            profile.UpdatedAt = _time.GetUtcNow().UtcDateTime;
            await _repo.SaveChangesAsync(ct);
            return true;
        }

        private async Task<PlayerStatProfile> GetOrAddProfileAsync(int userId, CancellationToken ct)
        {
            var profile = await _repo.GetProfileForUpdateAsync(userId, ct);
            if (profile != null) return profile;
            profile = new PlayerStatProfile { UserId = userId };
            _repo.AddProfile(profile);
            return profile;
        }
    }
}
