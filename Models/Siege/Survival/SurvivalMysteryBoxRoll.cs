using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Siege Survival phase 4 (DESIGN §8a "record one roll and at most one delivery per roll id", S39, S59;
// PandiO/knk-workspace#41, link S7a): one authoritative Mystery Box roll inside a running match, written by the
// plugin through the match-scoped roll endpoint. Runtime/history row like SiegeMatch (append-only, no
// [FormConfigurableEntity]). RollId is the plugin's idempotency id (unique): the same id answers the stored row.
//
// The picked entry, its rolled enchantments and the reel are stored as a snapshot (EntryJson = the
// SurvivalMysteryBoxEntryDto as served at roll time; the point/pool/entry ids are plain columns without foreign
// keys), so a retry answers the same DTO after the admin re-balanced the pool, and authoring never conflicts with
// match history. Cascades with the match; the user is Restrict (like SiegeMatchParticipant). Match currency is
// not on the API (DESIGN §8): PriceCharged is the audit value the plugin's MatchLedger charged before the roll.
public class SurvivalMysteryBoxRoll
{
    public int Id { get; set; }

    public string RollId { get; set; } = null!;

    public int SiegeMatchId { get; set; }
    public SiegeMatch SiegeMatch { get; set; } = null!;

    public int SurvivalMysteryBoxPointId { get; set; }
    public int SurvivalMysteryBoxPoolId { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public SurvivalMysteryBoxVariant Variant { get; set; }

    public int SurvivalMysteryBoxEntryId { get; set; }
    public string EntryJson { get; set; } = "{}";
    public string RolledEnchantmentsJson { get; set; } = "[]";
    public string ReelJson { get; set; } = "[]";

    public int PriceCharged { get; set; }

    public SurvivalMysteryBoxRollState State { get; set; } = SurvivalMysteryBoxRollState.Rolled;
    public DateTime RolledAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClaimedAt { get; set; }
    public DateTime? ForfeitedAt { get; set; }
    public SurvivalMysteryBoxForfeitReason? ForfeitReason { get; set; }

    public SurvivalMysteryBoxDelivery? Delivery { get; set; }
}

// The one delivery of a roll (unique per roll, DESIGN §8a): how the item reached the roller and when. Cascades
// with the roll. A second claim of the same roll answers the stored delivery instead of adding one.
public class SurvivalMysteryBoxDelivery
{
    public int Id { get; set; }

    public int SurvivalMysteryBoxRollId { get; set; }
    public SurvivalMysteryBoxRoll SurvivalMysteryBoxRoll { get; set; } = null!;

    public SurvivalMysteryBoxDeliveryKind Kind { get; set; }
    public DateTime DeliveredAt { get; set; } = DateTime.UtcNow;
}
