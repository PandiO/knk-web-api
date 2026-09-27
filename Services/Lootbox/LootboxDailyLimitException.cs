namespace knkwebapi_v2.Services.Lootbox;

/// <summary>
/// A claim over the per-player daily cap (docs/specs/lootboxes/DESIGN.md §3.3 step 3, D15); controllers answer 429
/// <c>{ code: "DailyLimit" | "DailyPickupLimit", scope, limit, resetsAt }</c>. The day is the UTC calendar day, so
/// <see cref="ResetsAt"/> is always the next 00:00 UTC.
/// </summary>
public class LootboxDailyLimitException : LootboxConflictException
{
    public const string OpenCode = "DailyLimit";
    // World boxes picked up (DESIGN.md §3.8): the same limits, counted on pickups; the token's later open counts too.
    public const string PickupCode = "DailyPickupLimit";
    public const string GlobalScope = "Global";
    public const string TypeScope = "Type";

    /// <summary><see cref="GlobalScope"/> (all types together) or <see cref="TypeScope"/> (this box's type).</summary>
    public string Scope { get; }
    public int Limit { get; }
    public DateTime ResetsAt { get; }

    public LootboxDailyLimitException(string scope, int limit, DateTime resetsAt, string code = OpenCode)
        : base(code, $"Daily limit of {limit} lootbox{(code == PickupCode ? " pickups" : "es")}{(scope == TypeScope ? " of this type" : "")} reached; it resets at 00:00 UTC.")
    {
        Scope = scope;
        Limit = limit;
        ResetsAt = resetsAt;
    }
}
