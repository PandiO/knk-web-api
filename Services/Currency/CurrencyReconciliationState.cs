using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The last reconciliation run since startup, and a gate so only one runs at a time (the
    /// scheduled run and a staff member's "run now" share it). Singleton, in memory: after a
    /// restart the page shows nothing until the monitor's first run a few seconds later. What a
    /// run found that matters is persisted anyway, as R1/R2 alerts.
    /// </summary>
    public class CurrencyReconciliationState
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private volatile CurrencyReconciliationRunDto? _last;

        public CurrencyReconciliationRunDto? LastRun => _last;

        public bool Running => _gate.CurrentCount == 0;

        /// <summary>Takes the gate if free; false when a run is already going on.</summary>
        public bool TryBegin() => _gate.Wait(0);

        public void Complete(CurrencyReconciliationRunDto run)
        {
            _last = run;
            _gate.Release();
        }
    }
}
