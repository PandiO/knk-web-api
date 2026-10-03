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

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>
    /// Reads and atomically updates a player's statistics visibility (KNG-34, DESIGN.md §F.4,
    /// IMPLEMENTATION_PLAN.md §3.1). The update locks the user row, compares every expected value
    /// first and writes only when all match (one transaction) — the group action of the in-game
    /// menu is one such update.
    /// </summary>
    public class StatisticsVisibilityService : IStatisticsVisibilityService
    {
        public const int MaxChanges = 64;

        private readonly IStatisticsRepository _repo;
        private readonly ICurrencyService _currency;
        private readonly TimeProvider _time;

        public StatisticsVisibilityService(IStatisticsRepository repo, ICurrencyService currency, TimeProvider? time = null)
        {
            _repo = repo;
            _currency = currency;
            _time = time ?? TimeProvider.System;
        }

        public async Task<StatisticsVisibilityDto?> GetAsync(int userId, CancellationToken ct = default)
        {
            var user = await _repo.GetUserAsync(userId, ct);
            if (user == null) return null;
            var rows = await _repo.GetVisibilityAsync(userId, ct: ct);
            return await BuildAsync(userId, rows, ct);
        }

        public async Task<StatisticsVisibilityDto?> UpdateAsync(int userId, StatisticsVisibilityUpdateDto update, CancellationToken ct = default)
        {
            var changes = Validate(update);
            var user = await _repo.GetUserAsync(userId, ct);
            if (user == null) return null;
            if (changes.Count == 0) return await GetAsync(userId, ct);

            await _repo.InTransactionAsync(async () =>
            {
                await _repo.LockUserAsync(userId, ct);
                var rows = await _repo.GetVisibilityAsync(userId, tracked: true, ct);

                var mismatch = changes.Any(c => Current(rows, c.SettingKey, c.Context) != c.Expected);
                if (mismatch)
                {
                    throw new StatisticsVisibilityConflictException(await BuildAsync(userId, rows, ct));
                }

                var now = _time.GetUtcNow().UtcDateTime;
                foreach (var change in changes)
                {
                    var row = rows.FirstOrDefault(r => r.SettingKey == change.SettingKey && r.ContextKey == change.Context);
                    if (row == null)
                    {
                        _repo.AddVisibility(new PlayerStatVisibility
                        {
                            UserId = userId,
                            SettingKey = change.SettingKey,
                            ContextKey = change.Context,
                            Visibility = change.Visibility,
                            UpdatedAt = now
                        });
                    }
                    else if (row.Visibility != change.Visibility)
                    {
                        row.Visibility = change.Visibility;
                        row.UpdatedAt = now;
                    }
                }
                await _repo.SaveChangesAsync(ct);
                return true;
            }, ct);

            return await GetAsync(userId, ct);
        }

        /// <summary>The value a change's <c>expected</c> is compared with: the context override, else
        /// the metric level (Nobody when never set).</summary>
        private static StatisticVisibility Current(IReadOnlyCollection<PlayerStatVisibility> rows, string settingKey, string context) =>
            context.Length == 0
                ? StatisticsVisibilityRules.MetricLevel(rows, settingKey)
                : StatisticsVisibilityRules.ForContext(rows, settingKey, context);

        private sealed record Change(string SettingKey, string Context, StatisticVisibility Expected, StatisticVisibility Visibility);

        private static List<Change> Validate(StatisticsVisibilityUpdateDto? update)
        {
            var changes = update?.Changes ?? new List<StatisticsVisibilityChangeDto>();
            if (changes.Count > MaxChanges)
            {
                throw new StatisticsValidationException("TooManyChanges", $"At most {MaxChanges} changes per update.");
            }
            var result = new List<Change>(changes.Count);
            var seen = new HashSet<(string, string)>();
            foreach (var change in changes)
            {
                var setting = StatisticsCatalog.FindSetting(change.SettingKey)
                    ?? throw new StatisticsValidationException("UnknownSetting", $"Unknown statistics setting '{change.SettingKey}'.");
                var context = change.Context ?? "";
                if (context.Length > 0)
                {
                    if (!setting.Contextual)
                    {
                        throw new StatisticsValidationException("NotContextual",
                            $"'{setting.SettingKey}' has no per-game-mode setting.");
                    }
                    if (!StatisticsCatalog.IsValidContext(context))
                    {
                        throw new StatisticsValidationException("InvalidContext", $"'{context}' is not a valid game context.");
                    }
                }
                if (!Enum.IsDefined(change.Expected) || !Enum.IsDefined(change.Visibility))
                {
                    throw new StatisticsValidationException("InvalidVisibility", "Visibility must be Nobody, Friends or Everyone.");
                }
                if (!seen.Add((setting.SettingKey, context)))
                {
                    throw new StatisticsValidationException("DuplicateChange",
                        $"'{setting.SettingKey}'{(context.Length > 0 ? " / " + context : "")} is changed twice.");
                }
                result.Add(new Change(setting.SettingKey, context, change.Expected, change.Visibility));
            }
            return result;
        }

        private async Task<StatisticsVisibilityDto> BuildAsync(int userId, IReadOnlyCollection<PlayerStatVisibility> rows, CancellationToken ct)
        {
            // Contexts with data, from the lifetime totals of the player and their merged identities.
            var ids = new List<int> { userId };
            ids.AddRange(await _currency.GetMergedAccountIdsAsync(userId, ct));
            var dataContexts = (await _repo.GetTotalsAsync(ids, ct))
                .Where(t => t.ContextKey != "")
                .Select(t => (Setting: StatisticsCatalog.FindMetric(t.MetricKey)?.SettingKey, t.ContextKey))
                .Where(x => x.Setting != null)
                .ToHashSet();

            var dto = new StatisticsVisibilityDto { UserId = userId, FriendsAvailable = false };
            foreach (var setting in StatisticsCatalog.Settings)
            {
                var item = new StatisticsVisibilitySettingDto
                {
                    SettingKey = setting.SettingKey,
                    Group = setting.Group,
                    Label = setting.Label,
                    Contextual = setting.Contextual,
                    Visibility = StatisticsVisibilityRules.MetricLevel(rows, setting.SettingKey)
                };
                if (setting.Contextual)
                {
                    var contexts = rows.Where(r => r.SettingKey == setting.SettingKey && r.ContextKey != "").Select(r => r.ContextKey)
                        .Concat(dataContexts.Where(d => d.Setting == setting.SettingKey).Select(d => d.ContextKey))
                        .Distinct()
                        .OrderBy(ContextOrder).ThenBy(c => c, StringComparer.Ordinal);
                    foreach (var context in contexts)
                    {
                        var overrideRow = rows.FirstOrDefault(r => r.SettingKey == setting.SettingKey && r.ContextKey == context);
                        item.Contexts.Add(new StatisticsVisibilityContextDto
                        {
                            Context = context,
                            Visibility = overrideRow?.Visibility ?? item.Visibility,
                            IsOverride = overrideRow != null
                        });
                    }
                }
                dto.Settings.Add(item);
            }
            return dto;
        }

        private static int ContextOrder(string context)
        {
            var index = StatisticsCatalog.KnownContexts.ToList().IndexOf(context);
            return index < 0 ? int.MaxValue : index;
        }
    }
}
