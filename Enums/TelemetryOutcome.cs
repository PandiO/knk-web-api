namespace knkwebapi_v2.Enums;

/// <summary>Outcome of a diagnostic event (DESIGN.md §F.12): what happened to the attempted action.</summary>
public enum TelemetryOutcome : byte
{
    Succeeded = 0,
    Denied = 1,
    Failed = 2,
    Info = 3
}

/// <summary>
/// Detail level of a diagnostic event (DESIGN.md §F.12): baseline events are recorded for every
/// player; enhanced events only for owner-chosen players or test runs, and expire sooner (§F.15).
/// </summary>
public enum TelemetryLevel : byte
{
    Baseline = 0,
    Enhanced = 1
}

/// <summary>Where a diagnostic event was produced.</summary>
public enum TelemetrySource : byte
{
    Plugin = 0,
    Api = 1
}
