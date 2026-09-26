using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// See <see cref="ICurrencyAlertService"/>. Alerts go to currency_alerts, ILogger (warning;
    /// error for Critical), the knk.currency.alerts counter, and an in-game CurrencyAlert
    /// notification for online staff (DESIGN.md §3.9). Scoped: uses the request's (or the
    /// monitor cycle's) KnKDbContext.
    /// </summary>
    public class CurrencyAlertService : ICurrencyAlertService
    {
        private readonly KnKDbContext _context;
        private readonly CurrencyReconciler _reconciler;
        private readonly CurrencyAnomalyDetector _detector;
        private readonly CurrencyReconciliationState _state;
        private readonly CurrencyMonitorSignals _signals;
        private readonly ILogger<CurrencyAlertService> _logger;
        private readonly CurrencyMonitorOptions _options;
        private readonly CurrencyMetrics? _metrics;
        private readonly IPlayerNotificationQueue? _notifications;

        public CurrencyAlertService(
            KnKDbContext context,
            CurrencyReconciler reconciler,
            CurrencyAnomalyDetector detector,
            CurrencyReconciliationState state,
            CurrencyMonitorSignals signals,
            ILogger<CurrencyAlertService> logger,
            IOptions<CurrencyMonitorOptions>? options = null,
            CurrencyMetrics? metrics = null,
            IPlayerNotificationQueue? notifications = null)
        {
            _context = context;
            _reconciler = reconciler;
            _detector = detector;
            _state = state;
            _signals = signals;
            _logger = logger;
            _options = options?.Value ?? new CurrencyMonitorOptions();
            _metrics = metrics;
            _notifications = notifications;
        }

        // ===== Raising =====

        public async Task<CurrencyAlert?> RaiseAsync(CurrencyAlertDraft draft, CancellationToken ct = default) =>
            (await RaiseCoreAsync(draft, ct)).Alert;

        private async Task<(CurrencyAlert? Alert, List<string> TransfersDisabled)> RaiseCoreAsync(CurrencyAlertDraft draft, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            if (draft.SuppressFor > TimeSpan.Zero)
            {
                var since = now - draft.SuppressFor;
                if (await _context.CurrencyAlerts.AsNoTracking()
                        .AnyAsync(a => a.Rule == draft.Rule && a.DedupKey == draft.DedupKey && a.CreatedAt >= since, ct))
                {
                    return (null, new List<string>());
                }
            }

            // R1 kill switch (DESIGN.md §4 D8): player transfers of the mismatched currency stop
            // until staff turn them back on in the currency policy. Grants and spends go on.
            var disabled = new List<string>();
            if (draft.Rule == CurrencyAlertRules.Reconciliation && _options.AutoDisableTransfersOnMismatch)
            {
                foreach (var currency in draft.DisableTransfersFor.Distinct())
                {
                    var policy = await _context.CurrencyPolicies.FirstOrDefaultAsync(p => p.Currency == currency, ct);
                    if (policy is { TransfersEnabled: true })
                    {
                        policy.TransfersEnabled = false;
                        policy.UpdatedAt = now;
                        policy.UpdatedByUserId = null;
                        disabled.Add(currency.ToString());
                    }
                }
            }

            var alert = new CurrencyAlert
            {
                Rule = draft.Rule,
                Severity = draft.Severity,
                UserId = draft.UserId,
                TransactionId = draft.TransactionId,
                Summary = CurrencyAnomalyDetector.Truncate(draft.Summary),
                DedupKey = draft.DedupKey.Length <= 200 ? draft.DedupKey : draft.DedupKey[..200],
                DetailsJson = DetailsJson(draft.Details, disabled),
                CreatedAt = now
            };
            _context.CurrencyAlerts.Add(alert);
            await _context.SaveChangesAsync(ct);

            var level = draft.Severity == CurrencyAlertSeverity.Critical ? LogLevel.Error : LogLevel.Warning;
            _logger.Log(level, "Currency alert {AlertId} {Rule} ({Severity}) user {UserId} tx {TransactionId}: {Summary}{Disabled}",
                alert.Id, alert.Rule, alert.Severity, alert.UserId, alert.TransactionId, alert.Summary,
                disabled.Count > 0 ? $" — transfers switched off for {string.Join(", ", disabled)}" : "");
            _metrics?.RecordAlert(alert.Rule, alert.Severity);
            await NotifyStaffAsync(alert, disabled, ct);
            return (alert, disabled);
        }

        private static string? DetailsJson(object? details, List<string> disabled)
        {
            var node = details == null ? null : JsonSerializer.SerializeToNode(details);
            if (disabled.Count > 0)
            {
                if (node is not JsonObject obj)
                {
                    obj = new JsonObject { ["details"] = node };
                }
                obj["transfersDisabled"] = new JsonArray(disabled.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray());
                node = obj;
            }
            return node?.ToJsonString();
        }

        private async Task NotifyStaffAsync(CurrencyAlert alert, List<string> disabled, CancellationToken ct)
        {
            if (_notifications == null)
            {
                return;
            }
            var min = Enum.TryParse<CurrencyAlertSeverity>(_options.InGameMinSeverity, ignoreCase: true, out var parsed)
                ? parsed
                : CurrencyAlertSeverity.Low;
            if (alert.Severity < min)
            {
                return;
            }
            try
            {
                string? username = null;
                if (alert.UserId.HasValue)
                {
                    username = await _context.Users.AsNoTracking().Where(u => u.Id == alert.UserId.Value)
                        .Select(u => u.Username).FirstOrDefaultAsync(ct);
                }
                _notifications.EnqueueCurrencyAlert(new CurrencyAlertNotificationDto
                {
                    AlertId = alert.Id,
                    Rule = alert.Rule,
                    RuleName = CurrencyAlertRules.NameOf(alert.Rule),
                    Severity = alert.Severity.ToString(),
                    Summary = alert.Summary,
                    UserId = alert.UserId,
                    Username = username,
                    TransfersDisabled = disabled
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort: the alert is stored and on the web page either way.
                _logger.LogWarning(ex, "Could not queue the in-game notice for currency alert {AlertId}", alert.Id);
            }
        }

        // ===== Monitor steps =====

        public async Task<int> RunRulesAsync(DateTime now, CancellationToken ct = default)
        {
            var rules = new (string Rule, Func<Task<List<CurrencyAlertDraft>>> Evaluate)[]
            {
                (CurrencyAlertRules.Funnel, () => _detector.FunnelAsync(now, ct)),
                (CurrencyAlertRules.PingPong, () => _detector.PingPongAsync(now, ct)),
                (CurrencyAlertRules.Velocity, () => _detector.VelocityAsync(now, ct)),
                (CurrencyAlertRules.Admin, () => _detector.AdminAsync(now, ct)),
                (CurrencyAlertRules.MintRate, () => _detector.MintRateAsync(now, ct))
            };
            var raised = 0;
            foreach (var (rule, evaluate) in rules)
            {
                try
                {
                    foreach (var draft in await evaluate())
                    {
                        if (await RaiseAsync(draft, ct) != null)
                        {
                            raised++;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One broken rule mustn't stop the others.
                    _logger.LogError(ex, "Currency monitor rule {Rule} failed", rule);
                    DiscardPendingChanges();
                }
            }
            return raised;
        }

        public async Task<int> FlushSignalsAsync(CancellationToken ct = default)
        {
            var raised = 0;
            foreach (var draft in _signals.Drain())
            {
                try
                {
                    if (await RaiseAsync(draft, ct) != null)
                    {
                        raised++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Could not store currency alert {Rule} {DedupKey}", draft.Rule, draft.DedupKey);
                    DiscardPendingChanges();
                }
            }
            return raised;
        }

        public async Task<CurrencyReconciliationRunDto?> RunReconciliationAsync(string trigger, int? triggeredByUserId, CancellationToken ct = default)
        {
            if (!_state.TryBegin())
            {
                return null;
            }
            var run = new CurrencyReconciliationRunDto
            {
                StartedAt = DateTime.UtcNow,
                Trigger = trigger,
                TriggeredByUserId = triggeredByUserId
            };
            var clock = Stopwatch.StartNew();
            try
            {
                var mismatches = await _reconciler.FindMismatchesAsync(null, ct);
                run.MismatchCount = mismatches.Count;
                run.Mismatches = mismatches.Take(CurrencyReconciliationRunDto.MaxListed).ToList();
                run.Truncated = mismatches.Count > CurrencyReconciliationRunDto.MaxListed;
                _metrics?.RecordReconciliation(mismatches.Count, clock.Elapsed);

                foreach (var draft in CurrencyAnomalyDetector.FromReconciliation(mismatches))
                {
                    var (alert, disabled) = await RaiseCoreAsync(draft, ct);
                    if (alert != null)
                    {
                        run.AlertIds.Add(alert.Id);
                    }
                    run.TransfersDisabled.AddRange(disabled);
                }
                if (mismatches.Count == 0)
                {
                    _logger.LogInformation("Currency reconciliation ({Trigger}): the ledger reconciles, {Ms} ms", trigger, clock.ElapsedMilliseconds);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                run.Error = ex.Message;
                _logger.LogError(ex, "Currency reconciliation ({Trigger}) failed", trigger);
                DiscardPendingChanges();
            }
            finally
            {
                run.FinishedAt = DateTime.UtcNow;
                run.DurationMs = clock.ElapsedMilliseconds;
                _state.Complete(run);
            }
            return run;
        }

        public CurrencyReconciliationStatusDto GetReconciliationStatus() => new()
        {
            LastRun = _state.LastRun,
            Running = _state.Running,
            MonitorEnabled = _options.Enabled,
            IntervalMinutes = _options.ReconciliationIntervalMinutes
        };

        /// <summary>A failed step's half-made changes must not ride along on the next save.</summary>
        private void DiscardPendingChanges()
        {
            foreach (var entry in _context.ChangeTracker.Entries<CurrencyAlert>().Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }
            foreach (var entry in _context.ChangeTracker.Entries<CurrencyPolicy>().Where(e => e.State == EntityState.Modified).ToList())
            {
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
            }
        }

        // ===== Staff reads =====

        public async Task<CurrencyAlertPageDto> ListAsync(CurrencyAlertQuery query, CancellationToken ct = default)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var alerts = _context.CurrencyAlerts.AsNoTracking().AsQueryable();
            switch (query.Status?.Trim().ToLowerInvariant())
            {
                case "acked":
                case "acknowledged":
                    alerts = alerts.Where(a => a.AckedAt != null);
                    break;
                case "all":
                    break;
                default:
                    alerts = alerts.Where(a => a.AckedAt == null);
                    break;
            }
            if (query.MinSeverity.HasValue)
            {
                var min = query.MinSeverity.Value;
                alerts = alerts.Where(a => a.Severity >= min);
            }
            if (!string.IsNullOrWhiteSpace(query.Rule))
            {
                var rule = query.Rule.Trim().ToUpperInvariant();
                alerts = alerts.Where(a => a.Rule == rule);
            }
            if (query.UserId.HasValue)
            {
                var userId = query.UserId.Value;
                alerts = alerts.Where(a => a.UserId == userId);
            }

            var total = await alerts.CountAsync(ct);
            var rows = await alerts
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync(ct);
            var open = await _context.CurrencyAlerts.AsNoTracking()
                .Where(a => a.AckedAt == null)
                .GroupBy(a => a.Severity)
                .Select(g => new { Severity = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            return new CurrencyAlertPageDto
            {
                Items = await ToDtosAsync(rows, ct),
                TotalCount = total,
                PageNumber = page,
                PageSize = pageSize,
                OpenCount = open.Sum(o => o.Count),
                OpenBySeverity = open.OrderByDescending(o => o.Severity).ToDictionary(o => o.Severity.ToString(), o => o.Count)
            };
        }

        public async Task<CurrencyAlertDto> AcknowledgeAsync(long alertId, int actorUserId, CancellationToken ct = default)
        {
            var alert = await _context.CurrencyAlerts.FirstOrDefaultAsync(a => a.Id == alertId, ct)
                ?? throw new KeyNotFoundException($"Currency alert {alertId} doesn't exist.");
            if (alert.AckedAt == null)
            {
                alert.AckedAt = DateTime.UtcNow;
                alert.AckedByUserId = actorUserId;
                await _context.SaveChangesAsync(ct);
                _logger.LogInformation("Currency alert {AlertId} ({Rule}) acknowledged by user {UserId}", alert.Id, alert.Rule, actorUserId);
            }
            return (await ToDtosAsync(new List<CurrencyAlert> { alert }, ct)).Single();
        }

        private async Task<List<CurrencyAlertDto>> ToDtosAsync(List<CurrencyAlert> alerts, CancellationToken ct)
        {
            var userIds = alerts.Select(a => a.UserId).Concat(alerts.Select(a => a.AckedByUserId))
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            var names = userIds.Count == 0
                ? new Dictionary<int, string>()
                : await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
            var txIds = alerts.Where(a => a.TransactionId.HasValue).Select(a => a.TransactionId!.Value).Distinct().ToList();
            var publicIds = txIds.Count == 0
                ? new Dictionary<long, string>()
                : await _context.CurrencyTransactions.AsNoTracking().Where(t => txIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.PublicId }).ToDictionaryAsync(t => t.Id, t => t.PublicId, ct);

            string? Name(int? id) => id.HasValue && names.TryGetValue(id.Value, out var name) ? name : null;

            return alerts.Select(a => new CurrencyAlertDto
            {
                Id = a.Id,
                Rule = a.Rule,
                RuleName = CurrencyAlertRules.NameOf(a.Rule),
                Severity = a.Severity.ToString(),
                Summary = a.Summary,
                UserId = a.UserId,
                Username = Name(a.UserId),
                TransactionId = a.TransactionId,
                TransactionPublicId = a.TransactionId.HasValue && publicIds.TryGetValue(a.TransactionId.Value, out var publicId) ? publicId : null,
                Details = ParseDetails(a.DetailsJson),
                CreatedAt = a.CreatedAt,
                AckedAt = a.AckedAt,
                AckedByUserId = a.AckedByUserId,
                AckedByUsername = Name(a.AckedByUserId)
            }).ToList();
        }

        private static JsonElement? ParseDetails(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                using var document = JsonDocument.Parse(json);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
