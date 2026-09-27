using System.Diagnostics.Metrics;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// OpenTelemetry instruments for currency flows (docs/specs/currency-payments/DESIGN.md §3.9,
    /// IMPLEMENTATION_PLAN.md Phase 5), on the meter <see cref="MeterName"/> that Program.cs adds
    /// to the existing WithMetrics pipeline. Singleton.
    /// <para>
    /// Labels are low-cardinality only (reason code, currency, kind, error code, rule, severity) —
    /// never a user id or amount (OBSERVABILITY.md privacy rules). Postings are counted when
    /// CurrencyService has written them; a caller that posts inside its own transaction and then
    /// rolls back has still been counted (rare: only failures after the posting).
    /// </para>
    /// </summary>
    public sealed class CurrencyMetrics : IDisposable
    {
        public const string MeterName = "Knk.Currency";

        private readonly Counter<long> _postings;
        private readonly Counter<long> _amount;
        private readonly Counter<long> _denials;
        private readonly Counter<long> _replays;
        private readonly Histogram<double> _lockWait;
        private readonly Counter<long> _alerts;
        private readonly Histogram<double> _reconciliationDuration;
        private long _lastMismatches = -1;

        public CurrencyMetrics()
        {
            Meter = new Meter(MeterName);
            _postings = Meter.CreateCounter<long>("knk.currency.postings", "{posting}",
                "Ledger postings written, per reason code, currency and kind (a posting of two currencies counts once per currency).");
            _amount = Meter.CreateCounter<long>("knk.currency.amount", "{unit}",
                "Amount moved per reason code and currency: minted for grants, burned for spends, sent for transfers.");
            _denials = Meter.CreateCounter<long>("knk.currency.denials", "{posting}",
                "Postings and transfers refused, per CurrencyErrorCode.");
            _replays = Meter.CreateCounter<long>("knk.currency.replays", "{posting}",
                "Retries answered with the stored result of an earlier posting (same idempotency key).");
            _lockWait = Meter.CreateHistogram<double>("knk.currency.lock_wait_ms", "ms",
                "Time a posting waited for its transaction and user row locks.");
            _alerts = Meter.CreateCounter<long>("knk.currency.alerts", "{alert}",
                "Currency anomaly alerts raised, per rule and severity.");
            _reconciliationDuration = Meter.CreateHistogram<double>("knk.currency.reconciliation.duration_ms", "ms",
                "How long one reconciliation run took.");
            Meter.CreateObservableGauge("knk.currency.reconciliation.mismatches", ObserveMismatches, "{mismatch}",
                "Mismatches found by the last reconciliation run (none reported before the first run).");
        }

        /// <summary>The meter, for tests that listen to it.</summary>
        public Meter Meter { get; }

        /// <summary>One written posting. <paramref name="userLegs"/>: (currency, signed amount) of each user leg.</summary>
        public void RecordPosting(string reasonCode, CurrencyTransactionKind kind, IEnumerable<(Currency Currency, long Amount)> userLegs)
        {
            foreach (var group in userLegs.GroupBy(l => l.Currency))
            {
                var credits = group.Where(l => l.Amount > 0).Sum(l => l.Amount);
                var debits = -group.Where(l => l.Amount < 0).Sum(l => l.Amount);
                var tags = new KeyValuePair<string, object?>[]
                {
                    new("reason", reasonCode),
                    new("currency", group.Key.ToString()),
                    new("kind", kind.ToString())
                };
                _postings.Add(1, tags);
                var moved = Math.Max(credits, debits);
                if (moved > 0)
                {
                    _amount.Add(moved, tags);
                }
            }
        }

        public void RecordReplay(string reasonCode) =>
            _replays.Add(1, new KeyValuePair<string, object?>("reason", reasonCode));

        public void RecordDenial(CurrencyErrorCode code) =>
            _denials.Add(1, new KeyValuePair<string, object?>("code", code.ToString()));

        public void RecordLockWait(TimeSpan wait) => _lockWait.Record(wait.TotalMilliseconds);

        public void RecordAlert(string rule, CurrencyAlertSeverity severity) =>
            _alerts.Add(1, new KeyValuePair<string, object?>("rule", rule), new KeyValuePair<string, object?>("severity", severity.ToString()));

        public void RecordReconciliation(int mismatches, TimeSpan duration)
        {
            Interlocked.Exchange(ref _lastMismatches, mismatches);
            _reconciliationDuration.Record(duration.TotalMilliseconds);
        }

        private IEnumerable<Measurement<long>> ObserveMismatches()
        {
            var last = Interlocked.Read(ref _lastMismatches);
            return last < 0 ? Array.Empty<Measurement<long>>() : new[] { new Measurement<long>(last) };
        }

        public void Dispose() => Meter.Dispose();
    }
}
