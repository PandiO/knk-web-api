using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Currency anomaly alerts and reconciliation (docs/specs/currency-payments/DESIGN.md §3.9,
    /// IMPLEMENTATION_PLAN.md Phase 5). Driven by CurrencyMonitorService; the staff routes in
    /// CurrencyMonitorController read and acknowledge. Nothing here changes a balance: a mismatch
    /// or anomaly is reported, never corrected (the only automatic action is R1's transfer kill
    /// switch, which staff turn back on in the currency policy).
    /// </summary>
    public interface ICurrencyAlertService
    {
        /// <summary>
        /// Stores a finding unless one with the same rule and dedup key was raised within its
        /// suppression window; logs it, counts it, tells online staff in-game, and for R1 switches
        /// transfers off. Null when suppressed.
        /// </summary>
        Task<CurrencyAlert?> RaiseAsync(CurrencyAlertDraft draft, CancellationToken ct = default);

        /// <summary>The ledger rules R3–R7 for the windows ending at <paramref name="now"/>; returns the alerts raised.</summary>
        Task<int> RunRulesAsync(DateTime now, CancellationToken ct = default);

        /// <summary>R8/R9 findings collected in memory since the last call; returns the alerts raised.</summary>
        Task<int> FlushSignalsAsync(CancellationToken ct = default);

        /// <summary>
        /// Runs the reconciler (R1/R2) and raises alerts for what it finds. Null when a run is
        /// already going on.
        /// </summary>
        Task<CurrencyReconciliationRunDto?> RunReconciliationAsync(string trigger, int? triggeredByUserId, CancellationToken ct = default);

        CurrencyReconciliationStatusDto GetReconciliationStatus();

        Task<CurrencyAlertPageDto> ListAsync(CurrencyAlertQuery query, CancellationToken ct = default);

        /// <summary>Marks the alert handled (repeatable: an acknowledged alert is returned unchanged).</summary>
        /// <exception cref="KeyNotFoundException">No such alert.</exception>
        Task<CurrencyAlertDto> AcknowledgeAsync(long alertId, int actorUserId, CancellationToken ct = default);
    }
}
