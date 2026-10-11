using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Telemetry
{
    public interface ITelemetryIngestionService
    {
        /// <summary>Validates plugin events and queues the valid, new ones (never waits for the database write).</summary>
        Task<TelemetryBatchResultDto> IngestAsync(IReadOnlyList<TelemetryEventDto> events, CancellationToken ct = default);

        /// <summary>
        /// Queues an event produced by the API itself (e.g. <c>api.request_failed</c>). Payload keys not
        /// allowlisted for the name are dropped. False when telemetry is disabled, the name is unknown or
        /// the queue is full.
        /// </summary>
        bool RecordApiEvent(string name, TelemetryOutcome outcome, string action, int? userId, string? correlationId,
            string? reasonCode, IReadOnlyDictionary<string, object?> payload);
    }

    /// <summary>
    /// Diagnostic event ingestion (KNG-34 link 6, IMPLEMENTATION_PLAN.md §3.3, DESIGN.md §F.12).
    /// Validates the envelope, allowlists payload keys per event name (<see cref="TelemetryEventCatalog"/>),
    /// truncates long strings, drops enhanced events for players that are not enhanced targets,
    /// dedupes by eventId (within the batch and against stored events) and hands the rest to the
    /// bounded <see cref="TelemetryWriteQueue"/>. Telemetry is never spooled or retried (L1-23).
    /// </summary>
    public sealed class TelemetryIngestionService : ITelemetryIngestionService
    {
        public const string ApiServerName = "api";

        private readonly ITelemetryRepository _repository;
        private readonly TelemetryWriteQueue _queue;
        private readonly TelemetryMetrics? _metrics;
        private readonly DiagnosticTelemetryOptions _options;
        private readonly Func<DateTime> _clock;
        private static long _apiSequence;

        public TelemetryIngestionService(ITelemetryRepository repository, TelemetryWriteQueue queue,
            IOptions<DiagnosticTelemetryOptions>? options = null, TelemetryMetrics? metrics = null)
            : this(repository, queue, options?.Value ?? new DiagnosticTelemetryOptions(), metrics, () => DateTime.UtcNow)
        {
        }

        public TelemetryIngestionService(ITelemetryRepository repository, TelemetryWriteQueue queue,
            DiagnosticTelemetryOptions options, TelemetryMetrics? metrics, Func<DateTime> clock)
        {
            _repository = repository;
            _queue = queue;
            _options = options;
            _metrics = metrics;
            _clock = clock;
        }

        /// <summary>The API's release identifier on its own events.</summary>
        public static string ApiVersion { get; } =
            typeof(TelemetryIngestionService).Assembly.GetName().Version?.ToString() ?? "0";

        public async Task<TelemetryBatchResultDto> IngestAsync(IReadOnlyList<TelemetryEventDto> events, CancellationToken ct = default)
        {
            var result = new TelemetryBatchResultDto();
            var now = _clock();
            var valid = new List<(int Index, TelemetryEvent Event)>();
            var seen = new HashSet<Guid>();
            HashSet<int>? enhancedUsers = null;
            HashSet<int>? enhancedRuns = null;

            for (var i = 0; i < events.Count; i++)
            {
                var dto = events[i];
                var code = Validate(dto, now, out var definition);
                if (code == null && definition!.Level == TelemetryLevel.Enhanced)
                {
                    if (enhancedUsers == null)
                    {
                        var targets = await _repository.GetActiveTargetsAsync(now, ct);
                        enhancedUsers = targets.Where(t => t.UserId != null).Select(t => t.UserId!.Value).ToHashSet();
                        enhancedRuns = targets.Where(t => t.TestRunId != null).Select(t => t.TestRunId!.Value).ToHashSet();
                    }
                    var allowed = (dto.UserId != null && enhancedUsers.Contains(dto.UserId.Value))
                                  || (dto.TestRunId != null && enhancedRuns!.Contains(dto.TestRunId.Value));
                    if (!allowed) code = "NotEnhancedTarget";
                }
                if (code != null)
                {
                    Reject(result, i, dto, code);
                    continue;
                }
                if (!seen.Add(dto.EventId))
                {
                    result.Duplicates++;
                    continue;
                }
                valid.Add((i, ToEntity(dto, definition!, now)));
            }

            if (valid.Count > 0)
            {
                var stored = await _repository.GetExistingEventIdsAsync(valid.Select(v => v.Event.EventId).ToList(), ct);
                foreach (var (_, entity) in valid)
                {
                    if (stored.Contains(entity.EventId))
                    {
                        result.Duplicates++;
                    }
                    else if (_queue.TryEnqueue(entity))
                    {
                        result.Accepted++;
                    }
                    else
                    {
                        result.Dropped++;
                    }
                }
            }
            _metrics?.RecordReceived("plugin", result.Accepted);
            return result;
        }

        public bool RecordApiEvent(string name, TelemetryOutcome outcome, string action, int? userId, string? correlationId,
            string? reasonCode, IReadOnlyDictionary<string, object?> payload)
        {
            if (!_options.Enabled) return false;
            var definition = TelemetryEventCatalog.Find(name);
            if (definition == null) return false;
            var now = _clock();
            var entity = new TelemetryEvent
            {
                EventId = Guid.NewGuid(),
                Name = name,
                SchemaVersion = 1,
                Level = definition.Level,
                Source = TelemetrySource.Api,
                OccurredAt = now,
                ReceivedAt = now,
                ServerName = ApiServerName,
                ServerSeq = Interlocked.Increment(ref _apiSequence),
                AppVersion = Truncate(ApiVersion, 32)!,
                UserId = userId,
                CorrelationId = correlationId != null && TelemetryEventCatalog.CodePattern.IsMatch(correlationId) ? correlationId : null,
                Feature = definition.Family,
                Action = Truncate(action, 64)!,
                Outcome = outcome,
                ReasonCode = reasonCode != null && TelemetryEventCatalog.CodePattern.IsMatch(reasonCode) ? reasonCode : null,
                PayloadJson = SerializePayload(definition, payload.ToDictionary(p => p.Key, p => ToElement(p.Value)))
            };
            var queued = _queue.TryEnqueue(entity);
            if (queued) _metrics?.RecordReceived("api", 1);
            return queued;
        }

        /// <summary>Null when valid; otherwise the rejection code.</summary>
        private string? Validate(TelemetryEventDto dto, DateTime now, out TelemetryEventDefinition? definition)
        {
            definition = null;
            if (dto == null || dto.EventId == Guid.Empty) return "InvalidEnvelope";
            definition = TelemetryEventCatalog.Find(dto.Name);
            if (definition == null) return "UnknownEvent";
            if (definition.Source != TelemetrySource.Plugin) return "NotPluginEvent";
            if (dto.Level != null && !string.Equals(dto.Level, definition.Level.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return "LevelMismatch";
            }
            if (dto.SchemaVersion < 1 || dto.SchemaVersion > short.MaxValue) return "InvalidEnvelope";
            if (string.IsNullOrWhiteSpace(dto.ServerName) || dto.ServerName.Length > 64) return "InvalidEnvelope";
            if (dto.AppVersion != null && dto.AppVersion.Length > 32) return "InvalidEnvelope";
            if (!Enum.TryParse<TelemetryOutcome>(dto.Outcome, true, out var outcome) || !Enum.IsDefined(outcome)
                || int.TryParse(dto.Outcome, out _))
            {
                return "InvalidOutcome";
            }
            if (!IsCodeOrNull(dto.ReasonCode) || !IsCodeOrNull(dto.CorrelationId) || !IsCodeOrNull(dto.Action)
                || !IsCodeOrNull(dto.Feature) || !IsCodeOrNull(dto.ObjectId)
                || (dto.Feature?.Length > 32) || (dto.ObjectType != null && (dto.ObjectType.Length > 32 || !IsCodeOrNull(dto.ObjectType))))
            {
                return "InvalidCode";
            }
            var occurred = AsUtc(dto.OccurredAt);
            if (occurred < now.AddDays(-Math.Max(1, _options.LateEventToleranceDays))) return "TooOld";
            if (occurred > now.AddSeconds(Math.Max(0, _options.FutureToleranceSeconds))) return "InFuture";
            return null;
        }

        private static bool IsCodeOrNull(string? value) => value == null || TelemetryEventCatalog.CodePattern.IsMatch(value);

        private void Reject(TelemetryBatchResultDto result, int index, TelemetryEventDto? dto, string code)
        {
            result.Rejected.Add(new TelemetryRejectionDto { Index = index, EventId = dto?.EventId, Code = code });
            _metrics?.RecordRejected(code);
        }

        private static TelemetryEvent ToEntity(TelemetryEventDto dto, TelemetryEventDefinition definition, DateTime now) => new()
        {
            EventId = dto.EventId,
            Name = definition.Name,
            SchemaVersion = (short)dto.SchemaVersion,
            Level = definition.Level,
            Source = TelemetrySource.Plugin,
            OccurredAt = AsUtc(dto.OccurredAt),
            ReceivedAt = now,
            ServerName = dto.ServerName!.Trim(),
            ServerSeq = dto.ServerSeq,
            AppVersion = dto.AppVersion ?? "",
            UserId = dto.UserId,
            SessionKey = dto.SessionKey == Guid.Empty ? null : dto.SessionKey,
            TestRunId = dto.TestRunId,
            MatchId = dto.MatchId,
            CorrelationId = dto.CorrelationId,
            Feature = string.IsNullOrEmpty(dto.Feature) ? definition.Family : dto.Feature,
            Action = string.IsNullOrEmpty(dto.Action) ? definition.Name[(definition.Family.Length + 1)..] : dto.Action,
            Outcome = Enum.Parse<TelemetryOutcome>(dto.Outcome!, true),
            ReasonCode = dto.ReasonCode,
            ObjectType = dto.ObjectType,
            ObjectId = dto.ObjectId,
            PayloadJson = SerializePayload(definition, dto.Payload)
        };

        /// <summary>
        /// Keeps allowlisted keys with scalar values (string, number, bool), strings cut to 128
        /// characters, at most 16 keys; null when nothing is left.
        /// </summary>
        public static string? SerializePayload(TelemetryEventDefinition definition, IReadOnlyDictionary<string, JsonElement>? payload)
        {
            if (payload == null || payload.Count == 0) return null;
            var kept = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var (key, value) in payload)
            {
                if (kept.Count >= TelemetryEventCatalog.MaxPayloadKeys) break;
                if (!definition.PayloadKeys.Contains(key)) continue;
                switch (value.ValueKind)
                {
                    case JsonValueKind.String:
                        kept[key] = Truncate(value.GetString(), TelemetryEventCatalog.MaxStringLength) ?? "";
                        break;
                    case JsonValueKind.Number:
                        kept[key] = value.TryGetInt64(out var l) ? l : value.GetDouble();
                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        kept[key] = value.GetBoolean();
                        break;
                    // Objects, arrays and nulls are dropped: scalar values only.
                }
            }
            return kept.Count == 0 ? null : JsonSerializer.Serialize(kept);
        }

        private static JsonElement ToElement(object? value) => JsonSerializer.SerializeToElement(value);

        private static string? Truncate(string? value, int max) =>
            value == null ? null : value.Length <= max ? value : value[..max];

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
