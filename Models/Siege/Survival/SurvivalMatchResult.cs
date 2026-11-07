using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Siege Survival phase 5 (knk-workspace docs/specs/siege-survival/DESIGN.md §3 "Persist both highest wave reached
// and last fully completed wave", §9 v1 per-kill settlement (S32, S62), §10 "API owns durable match results and
// permanent ledger grants", §11 result screen; PandiO/knk-workspace#41, link S8a): the durable result of one
// survival match, one row per Siege match (unique), written once from the plugin's SurvivalMatchResultDto (or by
// the abort hook with SettlesRewards = false). Runtime/history row like SiegeMatch (append-only, no
// [FormConfigurableEntity]). Cascades with the match; scenario/lobby/profile ids are plain columns (the match row
// already holds the real FKs; a deleted difficulty profile must not take match history with it).
//
// The settlement is one multi-leg ledger posting (reason SURVIVAL_REWARD, key siege-survival:{matchId}) whose legs
// are recomputed on the API from the exact accruals (floor once per player, §9); LedgerPostingPublicId / SettledAt
// record it. RequestFingerprint is the SHA-256 of the plugin-supplied fields, so a retry with the same body
// answers the stored row and a different body is a 409 ResultAlreadyStored.
public class SurvivalMatchResult
{
    public const string RewardPolicyV1PerKill = "v1-per-kill";

    public int Id { get; set; }

    public int SiegeMatchId { get; set; }
    public SiegeMatch SiegeMatch { get; set; } = null!;

    public int SiegeLobbyId { get; set; }
    public int SiegeScenarioId { get; set; }
    public int? DifficultyProfileId { get; set; }
    public DateTime? ConfigurationGeneratedAt { get; set; }
    public SurvivalEquipmentMode EquipmentMode { get; set; }

    public SurvivalEndReason EndReason { get; set; }
    public int ReachedWave { get; set; }
    public int CompletedWave { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public int PlayersAtStart { get; set; }
    public int PlayersAtEnd { get; set; }
    public int EarnedKills { get; set; }
    public int SystemKills { get; set; }

    public string RewardPolicyVersion { get; set; } = RewardPolicyV1PerKill;
    public bool SettlesRewards { get; set; }
    public int TotalCoinsSettled { get; set; }
    public int TotalXpSettled { get; set; }
    public string? LedgerKey { get; set; }
    public string? LedgerPostingPublicId { get; set; }
    public DateTime? SettledAt { get; set; }

    public SurvivalMatchResultSource Source { get; set; } = SurvivalMatchResultSource.Plugin;
    public string? RequestFingerprint { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SurvivalMatchPlayerResult> Players { get; set; } = new List<SurvivalMatchPlayerResult>();
}

// One participant's statistics in a survival match result (DESIGN §9, §11): kills and bonus (headshot/critical)
// kills, deaths (= ghost count, every death makes a ghost, §3b), repair work, the match-currency totals the plugin
// kept (DESIGN §8; the API never holds match currency), the exact accruals and the integers settled, and the
// Mystery Box counts taken from the S7a roll rows. Cascades with the result; the user is Restrict (like
// SiegeMatchParticipant and the roll rows). KitId is a plain column (the kit catalog is authoring; history keeps
// the id the player picked).
public class SurvivalMatchPlayerResult
{
    public int Id { get; set; }

    public int SurvivalMatchResultId { get; set; }
    public SurvivalMatchResult SurvivalMatchResult { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public bool PresentAtEnd { get; set; }
    public bool LeftEarly { get; set; }
    public DateTime? LeftAt { get; set; }
    public int? KitId { get; set; }

    public int Kills { get; set; }
    public int BonusKills { get; set; }
    public int Deaths { get; set; }
    public double RepairWork { get; set; }

    public long MatchCurrencyEarned { get; set; }
    public long MatchCurrencySpent { get; set; }
    public long FinalMatchBalance { get; set; }

    public decimal CoinsAccrued { get; set; }
    public decimal XpAccrued { get; set; }
    public int CoinsSettled { get; set; }
    public int XpSettled { get; set; }

    public int MysteryBoxRolls { get; set; }
    public int MysteryBoxClaims { get; set; }
    public int MysteryBoxForfeits { get; set; }
}

// DESIGN §10 "Runtime/checkpoint": the latest checkpoint the plugin put for a running match, one row per match
// (upsert). Telemetry and a recovery aid, not a resume source (S61: a restart aborts the match). The scalar columns
// are the queryable summary; PayloadJson holds the whole SurvivalMatchCheckpointDto as received. Cascades with the
// match.
public class SurvivalMatchCheckpoint
{
    public int Id { get; set; }

    public int SiegeMatchId { get; set; }
    public SiegeMatch SiegeMatch { get; set; } = null!;

    public int SiegeScenarioId { get; set; }
    public int? DifficultyProfileId { get; set; }
    public DateTime CapturedAt { get; set; }
    public SurvivalSubstate Substate { get; set; }
    public int Wave { get; set; }
    public int CompletedWave { get; set; }
    public int AliveEnemies { get; set; }
    public int QueuedEnemies { get; set; }
    public int Players { get; set; }
    public int Ghosts { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public int Revision { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
