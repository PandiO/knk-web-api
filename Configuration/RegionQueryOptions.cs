namespace knkwebapi_v2.Configuration;

/// <summary>
/// <c>RegionQueries</c> (PandiO/knk-workspace#988): the API's questions to a game server about a WorldGuard region over
/// the game hub (region geometry, the siege weapon spot site check). On by default: the queries only read the world.
/// With <see cref="Enabled"/> off nothing is sent, the game hub ignores answers, and the validation rules and the spot
/// save that use them say they could not check (a save is not blocked).
/// </summary>
public class RegionQueryOptions
{
    public const string SectionName = "RegionQueries";

    /// <summary><c>RegionQueries:Enabled</c>. Default on.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary><c>RegionQueries:TimeoutSeconds</c>: how long the API waits for an answer. Default 5; clamped to 1..30.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>The wait as used.</summary>
    public TimeSpan EffectiveTimeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 30));
}
