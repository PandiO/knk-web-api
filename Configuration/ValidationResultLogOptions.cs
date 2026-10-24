namespace knkwebapi_v2.Configuration;

/// <summary>
/// KNG-269: the result log that lets <c>ValidationService</c> skip a field validation rule run when the
/// rule and its inputs are unchanged since the last run for the same subject (the entity being edited or
/// the draft being filled). Section <c>ValidationResultLog</c> in appsettings.
/// </summary>
public sealed class ValidationResultLogOptions
{
    public const string SectionName = "ValidationResultLog";

    /// <summary>False runs every rule every time and writes nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long a stored result of a rule that reads state outside its inputs (the region rules read
    /// WorldGuard through the plugin) counts as current. A region can be redrawn without any form input
    /// changing, so such results expire; pure rules never do.
    /// </summary>
    public int ExternalStateTtlHours { get; set; } = 24;

    /// <summary>Rows older than this are purged opportunistically (drafts that were never finished leave rows behind).</summary>
    public int RetentionDays { get; set; } = 30;
}
