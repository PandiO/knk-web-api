using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Telemetry
{
    public interface ITelemetryQueryService
    {
        /// <summary>Owner search, newest first. Audited (TelemetryViewed).</summary>
        Task<TelemetryEventPageDto> SearchAsync(int readerUserId, TelemetrySearchFilter filter, string? before, int limit,
            CancellationToken ct = default);

        /// <summary>One event with its correlated events and links; null when unknown. Audited.</summary>
        Task<TelemetryEventDetailDto?> GetEventAsync(int readerUserId, Guid eventId, CancellationToken ct = default);

        /// <summary>A player's merged timeline (events + ledger + Siege) in a window; null for an unknown user. Audited.</summary>
        Task<TelemetryTimelineDto?> GetTimelineAsync(int readerUserId, int userId, DateTime from, DateTime to, int limit,
            CancellationToken ct = default);

        Task<TelemetryHealthDto> GetHealthAsync(CancellationToken ct = default);

        Task<TelemetryClientConfigDto> GetClientConfigAsync(CancellationToken ct = default);

        Task<List<TelemetryTestRunDto>> GetTestRunsAsync(CancellationToken ct = default);

        Task<TelemetryTestRunDto> StartTestRunAsync(int ownerUserId, string name, string? description, CancellationToken ct = default);

        /// <summary>Ends a run (idempotent); null when unknown.</summary>
        Task<TelemetryTestRunDto?> EndTestRunAsync(int id, CancellationToken ct = default);

        Task<List<EnhancedTargetDto>> GetEnhancedTargetsAsync(CancellationToken ct = default);

        /// <summary>Validated by the controller (exactly one target, future expiry); null when the user/run is unknown or the run ended.</summary>
        Task<EnhancedTargetDto?> AddEnhancedTargetAsync(int ownerUserId, int? userId, int? testRunId, DateTime expiresAt,
            CancellationToken ct = default);

        Task<bool> RemoveEnhancedTargetAsync(int id, CancellationToken ct = default);

        /// <summary>Parses an opaque search cursor; null when malformed.</summary>
        TelemetryCursor? ParseCursor(string? before);
    }

    /// <summary>
    /// Owner-only diagnostic reads and test-run/enhanced-target management (KNG-34 link 6,
    /// IMPLEMENTATION_PLAN.md §3.3, DESIGN.md §F.13). Every event read is written to the audit log
    /// (TelemetryViewed). Ledger and Siege facts are joined at read time from their authoritative
    /// tables — nothing is copied into the event store.
    /// </summary>
    public sealed class TelemetryQueryService : ITelemetryQueryService
    {
        public const int MaxLimit = 500;
        public const int RelatedLimit = 100;

        private readonly ITelemetryRepository _repository;
        private readonly TelemetryWriteQueue _queue;
        private readonly IAuditLogService _audit;
        private readonly DiagnosticTelemetryOptions _options;
        private readonly Func<DateTime> _clock;

        public TelemetryQueryService(ITelemetryRepository repository, TelemetryWriteQueue queue, IAuditLogService audit,
            IOptions<DiagnosticTelemetryOptions>? options = null)
            : this(repository, queue, audit, options?.Value ?? new DiagnosticTelemetryOptions(), () => DateTime.UtcNow)
        {
        }

        public TelemetryQueryService(ITelemetryRepository repository, TelemetryWriteQueue queue, IAuditLogService audit,
            DiagnosticTelemetryOptions options, Func<DateTime> clock)
        {
            _repository = repository;
            _queue = queue;
            _audit = audit;
            _options = options;
            _clock = clock;
        }

        public async Task<TelemetryEventPageDto> SearchAsync(int readerUserId, TelemetrySearchFilter filter, string? before, int limit,
            CancellationToken ct = default)
        {
            var take = Math.Clamp(limit, 1, MaxLimit);
            var rows = await _repository.SearchAsync(filter, ParseCursor(before), take + 1, ct);
            var page = rows.Take(take).ToList();
            var names = await _repository.GetUsernamesAsync(UserIds(page), ct);
            await AuditAsync(readerUserId, filter.UserId, "events", new Dictionary<string, object?>
            {
                ["userId"] = filter.UserId,
                ["sessionKey"] = filter.SessionKey,
                ["testRunId"] = filter.TestRunId,
                ["matchId"] = filter.MatchId,
                ["correlationId"] = filter.CorrelationId,
                ["name"] = filter.Name,
                ["from"] = filter.From,
                ["to"] = filter.To,
                ["returned"] = page.Count
            });
            return new TelemetryEventPageDto
            {
                Items = page.Select(e => ToView(e, names)).ToList(),
                NextBefore = rows.Count > take ? FormatCursor(page[^1]) : null
            };
        }

        public async Task<TelemetryEventDetailDto?> GetEventAsync(int readerUserId, Guid eventId, CancellationToken ct = default)
        {
            var entity = await _repository.GetEventAsync(eventId, ct);
            if (entity == null) return null;
            var related = entity.CorrelationId == null
                ? new List<TelemetryEvent>()
                : await _repository.GetByCorrelationAsync(entity.CorrelationId, entity.Id, RelatedLimit, ct);
            var ledger = entity.CorrelationId == null
                ? new List<string>()
                : await _repository.GetLedgerPublicIdsByCorrelationAsync(entity.CorrelationId, RelatedLimit, ct);
            var names = await _repository.GetUsernamesAsync(UserIds(related.Append(entity)), ct);
            await AuditAsync(readerUserId, entity.UserId, "event", new Dictionary<string, object?>
            {
                ["eventId"] = eventId,
                ["related"] = related.Count
            });
            return new TelemetryEventDetailDto
            {
                Event = ToView(entity, names),
                Related = related.Select(e => ToView(e, names)).ToList(),
                Links = new TelemetryEventLinksDto { LedgerTransactionPublicIds = ledger, SiegeMatchId = entity.MatchId }
            };
        }

        public async Task<TelemetryTimelineDto?> GetTimelineAsync(int readerUserId, int userId, DateTime from, DateTime to, int limit,
            CancellationToken ct = default)
        {
            var names = await _repository.GetUsernamesAsync(new[] { userId }, ct);
            if (!names.ContainsKey(userId)) return null;
            var take = Math.Clamp(limit, 1, MaxLimit * 4);
            var events = await _repository.GetUserEventsAsync(userId, from, to, take + 1, ct);
            var legs = await _repository.GetLedgerLegsAsync(userId, from, to, take + 1, ct);
            var siege = await _repository.GetSiegeParticipationsAsync(userId, from, to, take + 1, ct);
            var truncated = events.Count > take || legs.Count > take || siege.Count > take;

            var items = new List<(DateTime At, int Order, long Tie, TelemetryTimelineItemDto Item)>();
            foreach (var e in events.Take(take))
            {
                items.Add((e.OccurredAt, 1, e.ServerSeq, new TelemetryTimelineItemDto { Kind = "event", At = e.OccurredAt, Event = ToView(e, names) }));
            }
            foreach (var leg in legs.Take(take))
            {
                items.Add((leg.CreatedAt, 2, 0, new TelemetryTimelineItemDto
                {
                    Kind = "ledger",
                    At = leg.CreatedAt,
                    Ledger = new TelemetryLedgerItemDto
                    {
                        PublicId = leg.PublicId,
                        ReasonCode = leg.ReasonCode,
                        CorrelationId = leg.CorrelationId,
                        Currency = leg.Currency.ToString(),
                        Delta = leg.Delta
                    }
                }));
            }
            foreach (var p in siege.Take(take))
            {
                AddSiege(items, p, "joined", p.JoinedAt, from, to);
                if (p.LeftAt != null) AddSiege(items, p, "left", p.LeftAt.Value, from, to);
                else if (p.EndedAt != null) AddSiege(items, p, "ended", p.EndedAt.Value, from, to);
            }

            await AuditAsync(readerUserId, userId, "timeline", new Dictionary<string, object?>
            {
                ["from"] = from,
                ["to"] = to,
                ["returned"] = items.Count
            });
            return new TelemetryTimelineDto
            {
                UserId = userId,
                Username = names[userId],
                From = from,
                To = to,
                Truncated = truncated,
                Items = items.OrderBy(i => i.At).ThenBy(i => i.Order).ThenBy(i => i.Tie).Select(i => i.Item).ToList()
            };
        }

        private static void AddSiege(List<(DateTime, int, long, TelemetryTimelineItemDto)> items, TelemetrySiegeRow p, string kind,
            DateTime at, DateTime from, DateTime to)
        {
            if (at < from || at >= to) return;
            items.Add((at, 3, 0, new TelemetryTimelineItemDto
            {
                Kind = "siege",
                At = at,
                Siege = new TelemetrySiegeItemDto
                {
                    MatchId = p.MatchId,
                    Status = p.Status.ToString(),
                    Kind = kind,
                    TeamId = p.TeamId,
                    Kills = p.Kills,
                    Deaths = p.Deaths,
                    Captures = p.Captures
                }
            }));
        }

        public async Task<TelemetryHealthDto> GetHealthAsync(CancellationToken ct = default) => new()
        {
            Enabled = _options.Enabled,
            QueueDepth = _queue.Depth,
            QueueCapacity = _queue.Capacity,
            DroppedSinceStart = _queue.DroppedSinceStart,
            LastWriteAt = _queue.LastWriteAt,
            EventsLast24h = await _repository.CountSinceAsync(_clock().AddHours(-24), ct)
        };

        public async Task<TelemetryClientConfigDto> GetClientConfigAsync(CancellationToken ct = default)
        {
            var config = new TelemetryClientConfigDto
            {
                Enabled = _options.Enabled,
                BaselineEventNames = TelemetryEventCatalog.PluginEventNames(TelemetryLevel.Baseline),
                EnhancedEventNames = TelemetryEventCatalog.PluginEventNames(TelemetryLevel.Enhanced)
            };
            if (!_options.Enabled) return config;
            var now = _clock();
            var runs = await _repository.GetTestRunsAsync(ct);
            var active = runs.Where(r => r.EndedAt == null).Select(r => r.Id).ToList();
            var targets = await _repository.GetActiveTargetsAsync(now, ct);
            config.ActiveTestRunIds = active;
            config.EnhancedUserIds = targets.Where(t => t.UserId != null).Select(t => t.UserId!.Value).Distinct().OrderBy(i => i).ToList();
            config.EnhancedTestRunIds = targets.Where(t => t.TestRunId != null && active.Contains(t.TestRunId.Value))
                .Select(t => t.TestRunId!.Value).Distinct().OrderBy(i => i).ToList();
            return config;
        }

        public async Task<List<TelemetryTestRunDto>> GetTestRunsAsync(CancellationToken ct = default) =>
            (await _repository.GetTestRunsAsync(ct)).Select(ToDto).ToList();

        public async Task<TelemetryTestRunDto> StartTestRunAsync(int ownerUserId, string name, string? description, CancellationToken ct = default)
        {
            var run = new TelemetryTestRun
            {
                Name = name.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                StartedAt = _clock(),
                CreatedByUserId = ownerUserId
            };
            await _repository.AddTestRunAsync(run, ct);
            return ToDto(run);
        }

        public async Task<TelemetryTestRunDto?> EndTestRunAsync(int id, CancellationToken ct = default)
        {
            var run = await _repository.GetTestRunAsync(id, ct);
            if (run == null) return null;
            if (run.EndedAt == null)
            {
                run.EndedAt = _clock();
                await _repository.SaveChangesAsync(ct);
            }
            return ToDto(run);
        }

        public async Task<List<EnhancedTargetDto>> GetEnhancedTargetsAsync(CancellationToken ct = default)
        {
            var targets = await _repository.GetActiveTargetsAsync(_clock(), ct);
            var names = await _repository.GetUsernamesAsync(targets.Where(t => t.UserId != null).Select(t => t.UserId!.Value).ToList(), ct);
            return targets.Select(t => ToDto(t, names)).ToList();
        }

        public async Task<EnhancedTargetDto?> AddEnhancedTargetAsync(int ownerUserId, int? userId, int? testRunId, DateTime expiresAt,
            CancellationToken ct = default)
        {
            if (userId != null && !await _repository.UserExistsAsync(userId.Value, ct)) return null;
            if (testRunId != null)
            {
                var run = await _repository.GetTestRunAsync(testRunId.Value, ct);
                if (run == null || run.EndedAt != null) return null;
            }
            var target = new TelemetryEnhancedTarget
            {
                UserId = userId,
                TestRunId = testRunId,
                ExpiresAt = expiresAt,
                CreatedByUserId = ownerUserId,
                CreatedAt = _clock()
            };
            await _repository.AddTargetAsync(target, ct);
            var names = await _repository.GetUsernamesAsync(userId != null ? new[] { userId.Value } : Array.Empty<int>(), ct);
            return ToDto(target, names);
        }

        public async Task<bool> RemoveEnhancedTargetAsync(int id, CancellationToken ct = default)
        {
            var target = await _repository.GetTargetAsync(id, ct);
            if (target == null) return false;
            await _repository.RemoveTargetAsync(target, ct);
            return true;
        }

        public TelemetryCursor? ParseCursor(string? before)
        {
            if (string.IsNullOrWhiteSpace(before)) return null;
            var parts = before.Split('_');
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                return null;
            }
            return new TelemetryCursor(new DateTime(ticks, DateTimeKind.Utc), id);
        }

        private static string FormatCursor(TelemetryEvent e) =>
            string.Create(CultureInfo.InvariantCulture, $"{e.OccurredAt.Ticks}_{e.Id}");

        private async Task AuditAsync(int readerUserId, int? targetUserId, string endpoint, Dictionary<string, object?> details)
        {
            details["endpoint"] = endpoint;
            var compact = details.Where(d => d.Value != null).ToDictionary(d => d.Key, d => d.Value);
            await _audit.RecordAsync(readerUserId, targetUserId is > 0 ? targetUserId.Value : readerUserId,
                AuditAction.TelemetryViewed, JsonSerializer.Serialize(compact));
        }

        private static IReadOnlyCollection<int> UserIds(IEnumerable<TelemetryEvent> events) =>
            events.Where(e => e.UserId != null).Select(e => e.UserId!.Value).Distinct().ToList();

        public static TelemetryEventViewDto ToView(TelemetryEvent e, IReadOnlyDictionary<int, string>? names = null) => new()
        {
            Id = e.Id,
            EventId = e.EventId,
            Name = e.Name,
            SchemaVersion = e.SchemaVersion,
            Level = e.Level,
            Source = e.Source,
            OccurredAt = DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc),
            ReceivedAt = DateTime.SpecifyKind(e.ReceivedAt, DateTimeKind.Utc),
            ServerName = e.ServerName,
            ServerSeq = e.ServerSeq,
            AppVersion = e.AppVersion,
            UserId = e.UserId,
            Username = e.UserId != null && names != null && names.TryGetValue(e.UserId.Value, out var n) ? n : null,
            SessionKey = e.SessionKey,
            TestRunId = e.TestRunId,
            MatchId = e.MatchId,
            CorrelationId = e.CorrelationId,
            Feature = e.Feature,
            Action = e.Action,
            Outcome = e.Outcome,
            ReasonCode = e.ReasonCode,
            ObjectType = e.ObjectType,
            ObjectId = e.ObjectId,
            Payload = string.IsNullOrEmpty(e.PayloadJson)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(e.PayloadJson)
        };

        private static TelemetryTestRunDto ToDto(TelemetryTestRun r) => new()
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            StartedAt = r.StartedAt,
            EndedAt = r.EndedAt,
            CreatedByUserId = r.CreatedByUserId
        };

        private static EnhancedTargetDto ToDto(TelemetryEnhancedTarget t, IReadOnlyDictionary<int, string> names) => new()
        {
            Id = t.Id,
            UserId = t.UserId,
            Username = t.UserId != null && names.TryGetValue(t.UserId.Value, out var n) ? n : null,
            TestRunId = t.TestRunId,
            ExpiresAt = t.ExpiresAt,
            CreatedByUserId = t.CreatedByUserId,
            CreatedAt = t.CreatedAt
        };
    }
}
