using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    // Siege Survival phase 4 (knk-workspace docs/specs/siege-survival/DESIGN.md §8a; PandiO/knk-workspace#41, link
    // S7a): the match-scoped Mystery Box roll contract the plugin (link S7b) builds against:
    //   POST api/siege-survival/matches/{siegeMatchId}/mystery-box/{pointId}/roll        { rollId, userId, priceCharged }
    //   POST api/siege-survival/matches/{siegeMatchId}/mystery-box/rolls/{rollId}/claim  { deliveryKind }
    //   POST api/siege-survival/matches/{siegeMatchId}/mystery-box/rolls/{rollId}/forfeit { reason }
    //   GET  api/siege-survival/matches/{siegeMatchId}/mystery-box/rolls
    // Every answer is a SurvivalMysteryBoxRollDto. No balance: match currency lives in the plugin's MatchLedger
    // (DESIGN §8), priceCharged is an audit value.

    /// <summary>Body of the roll call. rollId is the plugin's idempotency id (the MatchLedger charge id): a repeat answers the stored roll.</summary>
    public class SurvivalMysteryBoxRollRequestDto
    {
        public const int RollIdMaxLength = 64;

        [Required][JsonPropertyName("rollId")] public string RollId { get; set; } = string.Empty;
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("priceCharged")] public int PriceCharged { get; set; }
    }

    public class SurvivalMysteryBoxClaimRequestDto
    {
        [JsonPropertyName("deliveryKind")] public SurvivalMysteryBoxDeliveryKind DeliveryKind { get; set; } = SurvivalMysteryBoxDeliveryKind.Inventory;
    }

    public class SurvivalMysteryBoxForfeitRequestDto
    {
        [JsonPropertyName("reason")] public SurvivalMysteryBoxForfeitReason Reason { get; set; } = SurvivalMysteryBoxForfeitReason.Timeout;
    }

    /// <summary>One enchantment that passed its own chance on an item entry (DESIGN §8a "optional enchantments per entry with their own chances").</summary>
    public class SurvivalMysteryBoxRolledEnchantmentDto
    {
        [JsonPropertyName("enchantmentDefinitionId")] public int EnchantmentDefinitionId { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
    }

    /// <summary>
    /// One roll as stored. entry is the picked pool entry exactly as served at roll time (a snapshot, so a retry answers
    /// the same item after the pool was edited); rolledEnchantments are the item entry's enchantments that passed
    /// (empty for an enchanted book: the book's enchantment is entry.enchantmentDefinitionId/enchantmentLevel); reel is
    /// the carousel's entry ids drawn with the real odds, the last one being the result (lootbox DESIGN §3.9: no fake
    /// near misses). state Rolled → Claimed (claimedAt, deliveryKind) or Forfeited (forfeitedAt, forfeitReason).
    /// </summary>
    public class SurvivalMysteryBoxRollDto
    {
        [JsonPropertyName("rollId")] public string RollId { get; set; } = string.Empty;
        [JsonPropertyName("siegeMatchId")] public int SiegeMatchId { get; set; }
        [JsonPropertyName("pointId")] public int PointId { get; set; }
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("variant")] public SurvivalMysteryBoxVariant Variant { get; set; }
        [JsonPropertyName("entry")] public SurvivalMysteryBoxEntryDto Entry { get; set; } = new();
        [JsonPropertyName("rolledEnchantments")] public List<SurvivalMysteryBoxRolledEnchantmentDto> RolledEnchantments { get; set; } = new();
        [JsonPropertyName("reel")] public List<int> Reel { get; set; } = new();
        [JsonPropertyName("priceCharged")] public int PriceCharged { get; set; }
        [JsonPropertyName("state")] public SurvivalMysteryBoxRollState State { get; set; }
        [JsonPropertyName("rolledAt")] public DateTime RolledAt { get; set; }
        [JsonPropertyName("claimedAt")] public DateTime? ClaimedAt { get; set; }
        [JsonPropertyName("deliveryKind")] public SurvivalMysteryBoxDeliveryKind? DeliveryKind { get; set; }
        [JsonPropertyName("forfeitedAt")] public DateTime? ForfeitedAt { get; set; }
        [JsonPropertyName("forfeitReason")] public SurvivalMysteryBoxForfeitReason? ForfeitReason { get; set; }
    }
}
