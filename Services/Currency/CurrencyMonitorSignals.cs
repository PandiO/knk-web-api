using System.Collections.Concurrent;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// In-memory hand-off of the findings that happen during a request rather than in the ledger
    /// (currency DESIGN.md §3.9): R8 (a posting refused at the balance cap — refused postings are
    /// never stored, so the ledger can't show them) and R9 (a player's transfers refused more than
    /// ProbingMaxDeniedPerHour times within an hour, counted at the controller). The currency
    /// monitor drains them into currency_alerts every cycle. Writing the alert straight away isn't
    /// safe: the refusal may happen inside a caller's transaction that is being rolled back.
    /// Singleton; lost on restart, like the counters themselves.
    /// </summary>
    public class CurrencyMonitorSignals
    {
        /// <summary>A monitor that stops draining can't grow this past a bound.</summary>
        public const int MaxPending = 500;

        private static readonly TimeSpan ProbingWindow = TimeSpan.FromHours(1);

        private readonly ConcurrentQueue<CurrencyAlertDraft> _pending = new();
        private readonly ConcurrentDictionary<int, SenderDenials> _denials = new();
        private readonly Func<DateTime> _utcNow;
        private readonly int _probingThreshold;

        public CurrencyMonitorSignals(IOptions<CurrencyMonitorOptions>? options = null, Func<DateTime>? utcNow = null)
        {
            _probingThreshold = Math.Max(1, (options?.Value ?? new CurrencyMonitorOptions()).ProbingMaxDeniedPerHour);
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>R8: a posting for <paramref name="reasonCode"/> was refused because <paramref name="userId"/>'s balance would pass its cap.</summary>
        public void RecordCapHit(int? userId, Currency? currency, string? reasonCode)
        {
            var what = currency?.ToString() ?? "balance";
            Enqueue(new CurrencyAlertDraft(CurrencyAlertRules.CapHit, CurrencyAlertSeverity.Medium,
                $"A {reasonCode ?? "posting"} was refused: the {what.ToLowerInvariant()} balance would pass its cap.",
                $"R8:user:{userId?.ToString() ?? "-"}:{what}:{reasonCode ?? "-"}", TimeSpan.FromHours(24))
            {
                UserId = userId,
                Details = new { reasonCode, currency = currency?.ToString() }
            });
        }

        /// <summary>
        /// R9: one refused transfer (or confirmation) of <paramref name="senderUserId"/>. Raises a
        /// finding when the count within the last hour passes the threshold, then stays quiet for
        /// that sender until the window has emptied again.
        /// </summary>
        public void RecordTransferDenial(int senderUserId, CurrencyErrorCode code)
        {
            if (senderUserId <= 0)
            {
                return;
            }
            var now = _utcNow();
            var state = _denials.GetOrAdd(senderUserId, _ => new SenderDenials());
            int count;
            bool raise;
            lock (state)
            {
                while (state.Times.Count > 0 && state.Times.Peek() <= now - ProbingWindow)
                {
                    state.Times.Dequeue();
                }
                if (state.Times.Count == 0)
                {
                    state.Codes.Clear();
                }
                state.Times.Enqueue(now);
                state.Codes[code] = state.Codes.GetValueOrDefault(code) + 1;
                count = state.Times.Count;
                raise = count > _probingThreshold && (state.RaisedAt == null || state.RaisedAt <= now - ProbingWindow);
                if (raise)
                {
                    state.RaisedAt = now;
                }
            }
            if (raise)
            {
                Dictionary<string, int> codes;
                lock (state)
                {
                    codes = state.Codes.ToDictionary(c => c.Key.ToString(), c => c.Value);
                }
                Enqueue(new CurrencyAlertDraft(CurrencyAlertRules.Probing, CurrencyAlertSeverity.Low,
                    $"{count} refused transfers within an hour.", $"R9:user:{senderUserId}", ProbingWindow)
                {
                    UserId = senderUserId,
                    Details = new { deniedLastHour = count, threshold = _probingThreshold, codes }
                });
            }
            PruneIdleSenders(now);
        }

        /// <summary>Takes every waiting finding (oldest first).</summary>
        public IReadOnlyList<CurrencyAlertDraft> Drain()
        {
            var drained = new List<CurrencyAlertDraft>();
            while (_pending.TryDequeue(out var draft))
            {
                drained.Add(draft);
            }
            return drained;
        }

        /// <summary>Waiting findings (tests, diagnostics).</summary>
        public int PendingCount => _pending.Count;

        private void Enqueue(CurrencyAlertDraft draft)
        {
            _pending.Enqueue(draft);
            while (_pending.Count > MaxPending && _pending.TryDequeue(out _))
            {
            }
        }

        private void PruneIdleSenders(DateTime now)
        {
            if (_denials.Count < 1000)
            {
                return;
            }
            foreach (var (sender, state) in _denials)
            {
                lock (state)
                {
                    if (state.Times.Count == 0 || state.Times.Last() <= now - ProbingWindow)
                    {
                        _denials.TryRemove(sender, out _);
                    }
                }
            }
        }

        private sealed class SenderDenials
        {
            public Queue<DateTime> Times { get; } = new();
            public Dictionary<CurrencyErrorCode, int> Codes { get; } = new();
            public DateTime? RaisedAt { get; set; }
        }
    }
}
