namespace knkwebapi_v2.Models;

public enum LocationRetentionFrequency : byte
{
    Daily = 0,
    Weekly = 1
}

/// <summary>
/// Singleton ("global") schedule and thresholds of the Location orphan check (KNG-80), editable
/// by staff with knk.admin.location.retention. Defaults are the developer's decisions of
/// 2026-10-09: weekly on Sunday 04:00 server time, a 7-day grace period, Kept items re-flagged
/// after 6 months.
/// </summary>
public class LocationRetentionSettings
{
    public string Id { get; set; } = "global";

    /// <summary>Run on the schedule at all ("Run check now" works either way).</summary>
    public bool ScheduleEnabled { get; set; } = true;

    public LocationRetentionFrequency Frequency { get; set; } = LocationRetentionFrequency.Weekly;

    /// <summary>Day of a weekly run (ignored for daily runs).</summary>
    public DayOfWeek RunDayOfWeek { get; set; } = DayOfWeek.Sunday;

    /// <summary>Time of day of a run, in the API server's local time zone, in minutes after midnight (240 = 04:00).</summary>
    public int RunAtMinuteOfDay { get; set; } = 240;

    /// <summary>Locations created less than this many days ago are skipped (still being wired up by a form).</summary>
    public int GracePeriodDays { get; set; } = 7;

    /// <summary>A Kept item is flagged for review again this many months after it was kept.</summary>
    public int KeptRecheckMonths { get; set; } = 6;

    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
}
