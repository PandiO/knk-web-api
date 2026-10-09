namespace knkwebapi_v2.Services.LocationRetention;

/// <summary>Singleton: one orphan check at a time, scheduled or manual (like CurrencyReconciliationState).</summary>
public sealed class LocationRetentionRunGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool Running => _gate.CurrentCount == 0;

    /// <summary>Takes the gate if free; false when a run is already going on.</summary>
    public bool TryBegin() => _gate.Wait(0);

    public void End() => _gate.Release();
}
