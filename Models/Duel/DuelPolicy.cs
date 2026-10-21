namespace knkwebapi_v2.Models;

/// <summary>
/// The cross-arena duel review rules (arena duels DESIGN.md §4.2, §10.3; KNG-213): signal thresholds and the
/// escalation rule. One row (Id 1), seeded with the design defaults by migration AddDuelMatches, edited in the
/// web app like CurrencyPolicy (UpdatedAt is the optimistic-concurrency version). A first flag never holds,
/// whatever these say (developer, 2026-10-20): EscalationFlagCount is at least 2.
/// </summary>
public class DuelPolicy
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>S1 one-sided damage: the loser dealt less than this % of the winner's damage.</summary>
    public bool S1Enabled { get; set; } = true;
    public int S1LoserDamagePercent { get; set; } = 10;

    /// <summary>S2 short fight: the fight lasted less than this many seconds.</summary>
    public bool S2Enabled { get; set; } = true;
    public int S2ShortFightSeconds { get; set; } = 10;

    /// <summary>S3 unarmed loser: own-gear mode, the loser had no weapon and no armour at fight start.</summary>
    public bool S3Enabled { get; set; } = true;

    /// <summary>S4 passive loser: the loser made at most this many attack attempts.</summary>
    public bool S4Enabled { get; set; } = true;
    public int S4MaxLoserAttackAttempts { get; set; } = 0;

    /// <summary>S5 one-way pair: the same player won every one of the pair's last N value-moving duels (this one included).</summary>
    public bool S5Enabled { get; set; } = true;
    public int S5PairDuels { get; set; } = 3;

    /// <summary>S6 funnel: the winner won value from at least N different opponents with accounts younger than
    /// S6YoungAccountDays, within S6WindowDays (this one included).</summary>
    public bool S6Enabled { get; set; } = true;
    public int S6MinYoungOpponents { get; set; } = 3;
    public int S6YoungAccountDays { get; set; } = 7;
    public int S6WindowDays { get; set; } = 7;

    /// <summary>Tier 2 (held) once the same pair, or the same beneficiary, reaches this many flags within
    /// EscalationWindowDays (this one included). At least 2.</summary>
    public int EscalationFlagCount { get; set; } = 2;
    public int EscalationWindowDays { get; set; } = 7;

    public DateTime UpdatedAt { get; set; } = CurrencyPolicy.VersionStamp(DateTime.UtcNow);
    public int? UpdatedByUserId { get; set; }
}
