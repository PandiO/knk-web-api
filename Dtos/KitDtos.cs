using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace knkwebapi_v2.Dtos
{
    // Full CRUD shape, including nested Contents (docs/specs/kits/IMPLEMENTATION_PLAN.md §1).
    public class KitDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("helmetId")]
        public int? HelmetId { get; set; }

        [JsonPropertyName("chestplateId")]
        public int? ChestplateId { get; set; }

        [JsonPropertyName("leggingsId")]
        public int? LeggingsId { get; set; }

        [JsonPropertyName("bootsId")]
        public int? BootsId { get; set; }

        [JsonPropertyName("shieldId")]
        public int? ShieldId { get; set; }

        [JsonPropertyName("handId")]
        public int? HandId { get; set; }

        // Read-only navigation objects for the FK fields above. The Kit FormConfiguration authors
        // its pickers on the navigation property ("Helmet", not "HelmetId"), so FormWizard's edit
        // mode needs these to pre-fill them - without them every picker loads empty and an untouched
        // submit nulls the FK. Ignored on create/update, where only the *Id fields are read.
        [JsonPropertyName("helmet")]
        public ItemBlueprintNavDto? Helmet { get; set; }

        [JsonPropertyName("chestplate")]
        public ItemBlueprintNavDto? Chestplate { get; set; }

        [JsonPropertyName("leggings")]
        public ItemBlueprintNavDto? Leggings { get; set; }

        [JsonPropertyName("boots")]
        public ItemBlueprintNavDto? Boots { get; set; }

        [JsonPropertyName("shield")]
        public ItemBlueprintNavDto? Shield { get; set; }

        [JsonPropertyName("hand")]
        public ItemBlueprintNavDto? Hand { get; set; }

        [JsonPropertyName("contents")]
        public List<KitContentDto> Contents { get; set; } = new();

        [JsonPropertyName("minTitleBracketId")]
        public int? MinTitleBracketId { get; set; }

        // Read-only, see Helmet above.
        [JsonPropertyName("minTitleBracket")]
        public KitTitleBracketNavDto? MinTitleBracket { get; set; }

        [JsonPropertyName("requiredPermissionGroupId")]
        public int? RequiredPermissionGroupId { get; set; }

        // Read-only, see Helmet above.
        [JsonPropertyName("requiredPermissionGroup")]
        public RelatedPermissionGroupDto? RequiredPermissionGroup { get; set; }

        [JsonPropertyName("requiredPermissionNode")]
        public string? RequiredPermissionNode { get; set; }

        [JsonPropertyName("grantOnFirstJoin")]
        public bool GrantOnFirstJoin { get; set; }

        [JsonPropertyName("cooldownSeconds")]
        public int CooldownSeconds { get; set; }

        [JsonPropertyName("costAmount")]
        public int? CostAmount { get; set; }

        [JsonPropertyName("costCurrency")]
        public string? CostCurrency { get; set; }

        [JsonPropertyName("isSinglePurchasePremium")]
        public bool IsSinglePurchasePremium { get; set; }

        [JsonPropertyName("premiumPriceGems")]
        public int? PremiumPriceGems { get; set; }
    }

    // Lightweight TitleBracket shape for KitDto.MinTitleBracket. TitleBracket has no single name
    // (it's gendered), so "name" carries the male form, which is what ObjectField displays.
    public class KitTitleBracketNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("maleName")]
        public string MaleName { get; set; } = string.Empty;

        [JsonPropertyName("femaleName")]
        public string FemaleName { get; set; } = string.Empty;

        [JsonPropertyName("minExperience")]
        public int MinExperience { get; set; }
    }

    // One Kit.Contents slot entry — (SlotIndex, ItemBlueprintId, Quantity), per DESIGN.md §2.2.
    public class KitContentDto
    {
        [JsonPropertyName("slotIndex")]
        public int SlotIndex { get; set; }

        [Required]
        [JsonPropertyName("itemBlueprintId")]
        public int ItemBlueprintId { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; } = 1;
    }

    // Per-kit availability summary for a given user — what both /kit list (in-game) and the
    // future player-profile "Grant Kit" UI render from, so denial messaging is identical on both
    // surfaces by construction (DESIGN.md §4.1's GetAvailableForUserAsync).
    public class KitAvailabilityDto
    {
        [JsonPropertyName("kitId")]
        public int KitId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("canClaim")]
        public bool CanClaim { get; set; }

        [JsonPropertyName("denialReason")]
        public string? DenialReason { get; set; }

        [JsonPropertyName("cooldownExpiresAt")]
        public DateTime? CooldownExpiresAt { get; set; }

        [JsonPropertyName("isPurchased")]
        public bool IsPurchased { get; set; }

        [JsonPropertyName("costAmount")]
        public int? CostAmount { get; set; }

        [JsonPropertyName("costCurrency")]
        public string? CostCurrency { get; set; }

        [JsonPropertyName("isSinglePurchasePremium")]
        public bool IsSinglePurchasePremium { get; set; }

        [JsonPropertyName("premiumPriceGems")]
        public int? PremiumPriceGems { get; set; }
    }

    // Resolved loadout returned by a successful claim/give — each named equipment field maps to
    // an ItemBlueprintId (or null if that slot isn't part of this Kit), and Contents is the
    // (SlotIndex, ItemBlueprintId, Quantity) list, NOT a plain item list, per DESIGN.md §4.1/§4.2 —
    // the plugin places each resolved item via the unified grant-placement algorithm.
    public class KitClaimResultDto
    {
        [JsonPropertyName("kitId")]
        public int KitId { get; set; }

        [JsonPropertyName("helmetId")]
        public int? HelmetId { get; set; }

        [JsonPropertyName("chestplateId")]
        public int? ChestplateId { get; set; }

        [JsonPropertyName("leggingsId")]
        public int? LeggingsId { get; set; }

        [JsonPropertyName("bootsId")]
        public int? BootsId { get; set; }

        [JsonPropertyName("shieldId")]
        public int? ShieldId { get; set; }

        [JsonPropertyName("handId")]
        public int? HandId { get; set; }

        [JsonPropertyName("contents")]
        public List<KitContentSlotDto> Contents { get; set; } = new();
    }

    // One resolved (SlotIndex, ItemBlueprintId, Quantity) entry within a KitClaimResultDto.
    public class KitContentSlotDto
    {
        [JsonPropertyName("slotIndex")]
        public int SlotIndex { get; set; }

        [JsonPropertyName("itemBlueprintId")]
        public int ItemBlueprintId { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }
    }

    // Result of a successful PurchaseKitAsync call (docs/specs/kits/IMPLEMENTATION_PLAN.md §2) -
    // purchasing and claiming are separate calls (DESIGN.md §4.1), so this deliberately does not
    // carry a resolved loadout the way KitClaimResultDto does.
    public class KitPurchaseResultDto
    {
        [JsonPropertyName("kitId")]
        public int KitId { get; set; }

        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("gemsPaid")]
        public int GemsPaid { get; set; }

        [JsonPropertyName("purchasedAt")]
        public DateTime PurchasedAt { get; set; }
    }

    // Body for POST api/Kits/{id}/give - actorUserId is deliberately NOT a field here; it's
    // resolved from the authenticated caller's JWT claims (DESIGN.md §4.6), never client-supplied.
    public class GiveKitRequestDto
    {
        [Required]
        [JsonPropertyName("targetUserId")]
        public int TargetUserId { get; set; }
    }
}
