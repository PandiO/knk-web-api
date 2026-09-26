namespace knkwebapi_v2.Services.Lootbox;

/// <summary>
/// A claim over the per-player daily cap (docs/specs/lootboxes/DESIGN.md §3.3 step 3, D15); controllers answer 429
/// <c>{ code: "DailyLimit", scope, limit, resetsAt }</c>. The day is the UTC calendar day, so
/// <see cref="ResetsAt"/> is always the next 00:00 UTC.
/// </summary>
public class LootboxDailyLimitException : LootboxConflictException
{
    public const string GlobalScope = "Global";
    public const string TypeScope = "Type";

    /// <summary><see cref="GlobalScope"/> (all types together) or <see cref="TypeScope"/> (this box's type).</summary>
    public string Scope { get; }
    public int Limit { get; }
    public DateTime ResetsAt { get; }

    public LootboxDailyLimitException(string scope, int limit, DateTime resetsAt)
        : base("DailyLimit", scope == TypeScope
            ? $"Daily limit of {limit} lootboxes of this type reached; it resets at 00:00 UTC."
            : $"Daily limit of {limit} lootboxes reached; it resets at 00:00 UTC.")
    {
        Scope = scope;
        Limit = limit;
        ResetsAt = resetsAt;
    }
}
