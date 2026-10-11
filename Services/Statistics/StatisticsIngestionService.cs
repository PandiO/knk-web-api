using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>
    /// Ingests plugin statistics batches (KNG-34, knk-workspace docs/specs/player-statistics/
    /// IMPLEMENTATION_PLAN.md §3.1). One transaction per batch: the batch row is inserted first,
    /// so a replay (same batch id) applies nothing. Entries are validated one by one; all accepted
    /// changes are pre-aggregated per row and written with multi-row upserts.
    /// <list type="bullet">
    /// <item>session <c>start</c>: new session row, <c>logins</c> +1 on the start's local day,
    /// <c>FirstSessionAt</c> = earliest start. Repeats of a known key change nothing.</item>
    /// <item>session <c>end</c>: sets EndedAt/EndReason once.</item>
    /// <item>durations: split at local midnight into active_playtime / afk_time daily rows, added
    /// to the session's seconds; the session's heartbeat moves to the interval end.</item>
    /// <item>counters (sum) / records (max) → daily + totals.</item>
    /// <item>pvpKills → <c>pvp_kills</c> for the killer + the kill-pair row, and the internal
    /// <c>pvp_kills.ranked</c> for the kills within Leaderboards:RepeatVictimDailyCap of that pair's
    /// day (stored pair rows of earlier batches included) — the leaderboard input (§F.11).</item>
    /// </list>
    /// </summary>
    public class StatisticsIngestionService : IStatisticsIngestionService
    {
        private readonly IStatisticsRepository _repo;
        private readonly StatisticsOptions _options;
        private readonly LeaderboardsOptions _leaderboards;
        private readonly StatisticsMetrics? _metrics;
        private readonly ILogger<StatisticsIngestionService> _logger;
        private readonly TimeProvider _time;

        public StatisticsIngestionService(IStatisticsRepository repo, IOptions<StatisticsOptions>? options = null,
            StatisticsMetrics? metrics = null, ILogger<StatisticsIngestionService>? logger = null, TimeProvider? time = null,
            IOptions<LeaderboardsOptions>? leaderboards = null)
        {
            _repo = repo;
            _options = options?.Value ?? new StatisticsOptions();
            _leaderboards = leaderboards?.Value ?? new LeaderboardsOptions();
            _metrics = metrics;
            _logger = logger ?? NullLogger<StatisticsIngestionService>.Instance;
            _time = time ?? TimeProvider.System;
        }

        public async Task<StatisticsBatchResultDto> IngestAsync(StatisticsBatchDto batch, CancellationToken ct = default)
        {
            if (batch == null) throw new ArgumentException("A batch body is required.");
            if (batch.BatchId == Guid.Empty) throw new ArgumentException("batchId is required.");
            var sessions = batch.Sessions ?? new List<StatisticsSessionEntryDto>();
            var durations = batch.Durations ?? new List<StatisticsDurationEntryDto>();
            var counters = batch.Counters ?? new List<StatisticsValueEntryDto>();
            var records = batch.Records ?? new List<StatisticsValueEntryDto>();
            var pvpKills = batch.PvpKills ?? new List<StatisticsPvpKillEntryDto>();
            var entryCount = sessions.Count + durations.Count + counters.Count + records.Count + pvpKills.Count;
            if (entryCount > _options.MaxBatchEntries)
            {
                throw new ArgumentException($"A batch may carry at most {_options.MaxBatchEntries} entries (got {entryCount}).");
            }
            var serverName = Truncate(batch.ServerName, 64);

            var result = await _repo.InTransactionAsync(async () =>
            {
                var outcome = new StatisticsBatchResultDto { BatchId = batch.BatchId };
                if (await _repo.BatchExistsAsync(batch.BatchId, ct))
                {
                    outcome.Duplicate = true;
                    return outcome;
                }

                var now = _time.GetUtcNow().UtcDateTime;
                var context = new BatchContext(this, now, serverName, outcome);
                await context.LoadAsync(sessions, durations, counters, records, pvpKills, ct);

                context.ApplySessionStarts(sessions);
                context.ApplyDurations(durations);
                context.ApplySessionEnds(sessions);
                context.ApplyValues("counters", counters, StatisticPluginInput.Counter);
                context.ApplyValues("records", records, StatisticPluginInput.Record);
                context.ApplyPvpKills(pvpKills);
                await context.ApplyRankedKillsAsync(ct);

                outcome.Rejected = outcome.Rejected.OrderBy(r => SectionOrder(r.Section)).ThenBy(r => r.Index).ToList();
                outcome.Accepted = entryCount - outcome.Rejected.Count;

                var inserted = await _repo.TryAddBatchAsync(new PlayerStatBatch
                {
                    BatchId = batch.BatchId,
                    ServerName = serverName,
                    ReceivedAt = now,
                    EntryCount = entryCount,
                    RejectedCount = outcome.Rejected.Count
                }, ct);
                if (!inserted)
                {
                    // A concurrent request with the same batch id won the insert; it applies the batch.
                    return new StatisticsBatchResultDto { BatchId = batch.BatchId, Duplicate = true };
                }

                await _repo.ApplyAsync(context.Deltas, now, ct);
                return outcome;
            }, ct);

            if (result.Duplicate)
            {
                _metrics?.RecordDuplicate();
            }
            else
            {
                _metrics?.RecordBatch(result.Accepted, result.Rejected.Select(r => r.Code));
                if (result.Rejected.Count > 0)
                {
                    _logger.LogInformation("Statistics batch {BatchId} from {Server}: {Accepted} accepted, {Rejected} rejected ({Codes})",
                        batch.BatchId, serverName, result.Accepted, result.Rejected.Count,
                        string.Join(", ", result.Rejected.GroupBy(r => r.Code).Select(g => $"{g.Key}×{g.Count()}")));
                }
            }
            return result;
        }

        private static int SectionOrder(string section) => section switch
        {
            "sessions" => 0,
            "durations" => 1,
            "counters" => 2,
            "records" => 3,
            _ => 4
        };

        private static string Truncate(string? value, int max)
        {
            var text = value?.Trim() ?? "";
            return text.Length <= max ? text : text[..max];
        }

        /// <summary>Per-batch working state: lookups done once, accepted changes collected.</summary>
        private sealed class BatchContext
        {
            private readonly StatisticsIngestionService _owner;
            private readonly DateTime _now;
            private readonly string _serverName;
            private readonly StatisticsBatchResultDto _outcome;
            private readonly TimeZoneInfo _zone;
            private HashSet<int> _users = new();
            private Dictionary<Guid, PlayerStatSession> _sessions = new();
            private Dictionary<int, PlayerStatProfile> _profiles = new();

            public BatchContext(StatisticsIngestionService owner, DateTime now, string serverName, StatisticsBatchResultDto outcome)
            {
                _owner = owner;
                _now = now;
                _serverName = serverName;
                _outcome = outcome;
                _zone = StatisticsPeriods.FindZone(owner._options.TimeZone);
            }

            public StatisticsDeltaSet Deltas { get; } = new();

            /// <summary>Latest kill time per accepted kill pair of this batch (ranked-kill timestamps).</summary>
            private readonly Dictionary<KillPairKey, DateTime> _pairTimes = new();

            private IStatisticsRepository Repo => _owner._repo;

            public async Task LoadAsync(List<StatisticsSessionEntryDto> sessions, List<StatisticsDurationEntryDto> durations,
                List<StatisticsValueEntryDto> counters, List<StatisticsValueEntryDto> records, List<StatisticsPvpKillEntryDto> pvpKills,
                CancellationToken ct)
            {
                var userIds = sessions.Select(s => s.UserId)
                    .Concat(durations.Select(d => d.UserId))
                    .Concat(counters.Select(c => c.UserId))
                    .Concat(records.Select(r => r.UserId))
                    .Concat(pvpKills.Select(p => p.KillerUserId))
                    .Concat(pvpKills.Select(p => p.VictimUserId))
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();
                _users = await Repo.GetExistingUserIdsAsync(userIds, ct);
                _sessions = await Repo.GetSessionsAsync(
                    sessions.Select(s => s.SessionKey).Concat(durations.Select(d => d.SessionKey)).Where(k => k != Guid.Empty).ToList(), ct);
                _profiles = await Repo.GetProfilesForUpdateAsync(
                    sessions.Where(s => IsType(s, "start")).Select(s => s.UserId).Where(_users.Contains).ToList(), ct);
            }

            public void ApplySessionStarts(List<StatisticsSessionEntryDto> sessions)
            {
                for (var i = 0; i < sessions.Count; i++)
                {
                    var entry = sessions[i];
                    if (IsType(entry, "end")) continue;
                    if (!IsType(entry, "start") || entry.SessionKey == Guid.Empty)
                    {
                        Reject("sessions", i, StatisticsRejectionCodes.InvalidEntry);
                        continue;
                    }
                    if (!CheckUser("sessions", i, entry.UserId) || !CheckTime("sessions", i, entry.At)) continue;

                    if (_sessions.TryGetValue(entry.SessionKey, out var known))
                    {
                        if (known.UserId != entry.UserId) Reject("sessions", i, StatisticsRejectionCodes.UnknownSession);
                        continue; // a repeated start is a no-op
                    }

                    var at = Utc(entry.At);
                    var session = new PlayerStatSession
                    {
                        SessionKey = entry.SessionKey,
                        UserId = entry.UserId,
                        StartedAt = at,
                        LastHeartbeatAt = at,
                        ServerName = _serverName
                    };
                    Repo.AddSession(session);
                    _sessions[entry.SessionKey] = session;

                    Deltas.Add(entry.UserId, StatisticsPeriods.LocalDay(at, _zone), StatisticsCatalog.Logins, "",
                        StatisticAggregation.Sum, 1m, at);

                    if (!_profiles.TryGetValue(entry.UserId, out var profile))
                    {
                        profile = new PlayerStatProfile { UserId = entry.UserId, FirstSessionAt = at, UpdatedAt = _now };
                        Repo.AddProfile(profile);
                        _profiles[entry.UserId] = profile;
                    }
                    else if (profile.FirstSessionAt == null || at < profile.FirstSessionAt)
                    {
                        profile.FirstSessionAt = at;
                        profile.UpdatedAt = _now;
                    }
                }
            }

            public void ApplyDurations(List<StatisticsDurationEntryDto> durations)
            {
                for (var i = 0; i < durations.Count; i++)
                {
                    var entry = durations[i];
                    var metric = entry.Metric;
                    if (metric != StatisticsCatalog.ActivePlaytime && metric != StatisticsCatalog.AfkTime)
                    {
                        Reject("durations", i, StatisticsRejectionCodes.UnknownMetric);
                        continue;
                    }
                    var from = Utc(entry.From);
                    var to = Utc(entry.To);
                    if (to <= from || (to - from).TotalSeconds > (double)StatisticsCatalog.MaxDurationSeconds)
                    {
                        Reject("durations", i, StatisticsRejectionCodes.InvalidInterval);
                        continue;
                    }
                    if (!CheckUser("durations", i, entry.UserId) || !CheckTime("durations", i, from) || !CheckTime("durations", i, to)) continue;
                    if (!_sessions.TryGetValue(entry.SessionKey, out var session) || session.UserId != entry.UserId || from < session.StartedAt)
                    {
                        Reject("durations", i, StatisticsRejectionCodes.UnknownSession);
                        continue;
                    }

                    foreach (var (day, seconds) in StatisticsPeriods.SplitByDay(from, to, _zone))
                    {
                        Deltas.Add(entry.UserId, day, metric, "", StatisticAggregation.Sum, (decimal)seconds, to);
                    }
                    var total = (int)Math.Round((to - from).TotalSeconds, MidpointRounding.AwayFromZero);
                    if (metric == StatisticsCatalog.ActivePlaytime) session.ActiveSeconds += total;
                    else session.AfkSeconds += total;
                    if (to > session.LastHeartbeatAt) session.LastHeartbeatAt = to;
                    if (session.EndReason == PlayerSessionEndReason.Timeout && to > session.EndedAt)
                    {
                        // The API closed it for lack of heartbeats, but the plugin was still counting
                        // (e.g. the API was down): the session goes on.
                        session.EndedAt = null;
                        session.EndReason = null;
                    }
                }
            }

            public void ApplySessionEnds(List<StatisticsSessionEntryDto> sessions)
            {
                for (var i = 0; i < sessions.Count; i++)
                {
                    var entry = sessions[i];
                    if (!IsType(entry, "end")) continue;
                    var reason = entry.EndReason switch
                    {
                        "Quit" => PlayerSessionEndReason.Quit,
                        "ServerStop" => PlayerSessionEndReason.ServerStop,
                        "Kick" => PlayerSessionEndReason.Kick,
                        null => PlayerSessionEndReason.Quit,
                        _ => (PlayerSessionEndReason?)null
                    };
                    if (reason == null || entry.SessionKey == Guid.Empty)
                    {
                        Reject("sessions", i, StatisticsRejectionCodes.InvalidEntry);
                        continue;
                    }
                    if (!CheckUser("sessions", i, entry.UserId) || !CheckTime("sessions", i, entry.At)) continue;
                    if (!_sessions.TryGetValue(entry.SessionKey, out var session) || session.UserId != entry.UserId)
                    {
                        Reject("sessions", i, StatisticsRejectionCodes.UnknownSession);
                        continue;
                    }
                    var at = Utc(entry.At);
                    if (session.EndedAt != null && session.EndReason != PlayerSessionEndReason.Timeout) continue; // ended already
                    session.EndedAt = at < session.StartedAt ? session.StartedAt : at;
                    session.EndReason = reason;
                    if (session.EndedAt > session.LastHeartbeatAt) session.LastHeartbeatAt = session.EndedAt.Value;
                }
            }

            public void ApplyValues(string section, List<StatisticsValueEntryDto> entries, StatisticPluginInput input)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var metric = StatisticsCatalog.FindMetric(entry.Metric);
                    if (metric == null)
                    {
                        Reject(section, i, StatisticsRejectionCodes.UnknownMetric);
                        continue;
                    }
                    var context = entry.Context ?? "";
                    if (!CheckContext(section, i, metric, context)) continue;
                    if (metric.PluginInput != input || !StatisticsCatalog.IsPluginWritable(metric.Key, context))
                    {
                        Reject(section, i, StatisticsRejectionCodes.NotPluginWritable);
                        continue;
                    }
                    if (entry.Value < 0 || entry.Value > metric.MaxPerEntry || (input == StatisticPluginInput.Counter && entry.Value == 0))
                    {
                        Reject(section, i, StatisticsRejectionCodes.OutOfRange);
                        continue;
                    }
                    if (!CheckUser(section, i, entry.UserId) || !CheckTime(section, i, entry.OccurredAt)) continue;

                    var at = Utc(entry.OccurredAt);
                    Deltas.Add(entry.UserId, StatisticsPeriods.LocalDay(at, _zone), metric.Key, context, metric.Aggregation,
                        Math.Round(entry.Value, 4), at);
                }
            }

            public void ApplyPvpKills(List<StatisticsPvpKillEntryDto> kills)
            {
                var metric = StatisticsCatalog.FindMetric(StatisticsCatalog.PvpKills)!;
                for (var i = 0; i < kills.Count; i++)
                {
                    var entry = kills[i];
                    var context = entry.Context ?? "";
                    if (!CheckContext("pvpKills", i, metric, context)) continue;
                    if (!StatisticsCatalog.IsPluginWritable(metric.Key, context))
                    {
                        Reject("pvpKills", i, StatisticsRejectionCodes.NotPluginWritable);
                        continue;
                    }
                    if (entry.KillerUserId == entry.VictimUserId)
                    {
                        Reject("pvpKills", i, StatisticsRejectionCodes.InvalidEntry);
                        continue;
                    }
                    if (!CheckUser("pvpKills", i, entry.KillerUserId) || !CheckUser("pvpKills", i, entry.VictimUserId)
                        || !CheckTime("pvpKills", i, entry.OccurredAt)) continue;

                    var at = Utc(entry.OccurredAt);
                    var day = StatisticsPeriods.LocalDay(at, _zone);
                    Deltas.Add(entry.KillerUserId, day, metric.Key, context, StatisticAggregation.Sum, 1m, at);
                    Deltas.AddKillPair(entry.KillerUserId, entry.VictimUserId, day, context);
                    var pair = new KillPairKey(entry.KillerUserId, entry.VictimUserId, day, context);
                    _pairTimes[pair] = _pairTimes.TryGetValue(pair, out var latest) && latest > at ? latest : at;
                }
            }

            /// <summary>
            /// Adds pvp_kills.ranked for the kills of each (killer, victim, day, context) that still fit
            /// under the repeat-victim cap, counting the pair's stored kills of earlier batches first.
            /// </summary>
            public async Task ApplyRankedKillsAsync(CancellationToken ct)
            {
                if (Deltas.KillPairs.Count == 0) return;
                var cap = _owner._leaderboards.RepeatVictimDailyCap;
                var stored = cap > 0
                    ? await Repo.GetKillPairCountsAsync(Deltas.KillPairs.Keys.ToList(), ct)
                    : new Dictionary<KillPairKey, int>();
                foreach (var (pair, count) in Deltas.KillPairs)
                {
                    var ranked = cap > 0 ? Math.Min(count, Math.Max(0, cap - stored.GetValueOrDefault(pair))) : count;
                    if (ranked <= 0) continue;
                    Deltas.Add(pair.KillerUserId, pair.Day, StatisticsCatalog.PvpKillsRanked, pair.ContextKey,
                        StatisticAggregation.Sum, ranked, _pairTimes[pair]);
                }
            }

            private bool CheckContext(string section, int index, StatisticMetricDefinition metric, string context)
            {
                var valid = metric.Contextual ? StatisticsCatalog.IsValidContext(context) : context.Length == 0;
                if (!valid) Reject(section, index, StatisticsRejectionCodes.InvalidContext);
                return valid;
            }

            private bool CheckUser(string section, int index, int userId)
            {
                if (_users.Contains(userId)) return true;
                Reject(section, index, StatisticsRejectionCodes.UnknownUser);
                return false;
            }

            private bool CheckTime(string section, int index, DateTime value)
            {
                var at = Utc(value);
                if (at < _now.AddDays(-_owner._options.LateEventToleranceDays))
                {
                    Reject(section, index, StatisticsRejectionCodes.TooOld);
                    return false;
                }
                if (at > _now.AddSeconds(_owner._options.FutureToleranceSeconds))
                {
                    Reject(section, index, StatisticsRejectionCodes.InFuture);
                    return false;
                }
                return true;
            }

            private void Reject(string section, int index, string code) =>
                _outcome.Rejected.Add(new StatisticsRejectedEntryDto { Section = section, Index = index, Code = code });

            private static bool IsType(StatisticsSessionEntryDto entry, string type) =>
                string.Equals(entry.Type, type, StringComparison.OrdinalIgnoreCase);

            private static DateTime Utc(DateTime value) => value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }
    }
}
