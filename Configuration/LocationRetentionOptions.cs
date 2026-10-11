namespace knkwebapi_v2.Configuration;

/// <summary>
/// Host-level knobs of the Location orphan check (KNG-80), section "LocationRetention" in
/// appsettings; every value has a default, so the section is optional. The schedule itself
/// (day, time, grace period, Keep recheck) lives in the LocationRetentionSettings row so staff
/// can change it from the web panel.
/// </summary>
public class LocationRetentionOptions
{
    public const string SectionName = "LocationRetention";

    /// <summary>Run the scheduler at all ("Run check now" works either way).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Wait after startup before the first schedule check, so the API is up first.</summary>
    public int StartupDelaySeconds { get; set; } = 60;

    /// <summary>How often the scheduler looks whether a run is due.</summary>
    public int CheckIntervalMinutes { get; set; } = 5;

    /// <summary>Locations read per orphan query (keyset batches, read only).</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>IANA/Windows time zone the run time is in; empty = the API server's local time zone.</summary>
    public string? TimeZoneId { get; set; }
}
