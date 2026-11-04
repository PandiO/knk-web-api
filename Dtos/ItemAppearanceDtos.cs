using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// One banner pattern layer of a shield on an item blueprint, item instance or lootbox claim
    /// (PandiO/knk-workspace#232). Layers are listed bottom to top; the position in the list is the order.
    /// </summary>
    public class ItemBannerLayerDto
    {
        /// <summary>Banner pattern registry key, e.g. "minecraft:stripe_top" (GET /api/BannerDesigns/pattern-keys lists them).</summary>
        [JsonPropertyName("patternKey")]
        public string PatternKey { get; set; } = string.Empty;

        /// <summary>Dye colour name as org.bukkit.DyeColor spells it, e.g. "RED", "LIGHT_BLUE".</summary>
        [JsonPropertyName("color")]
        public string Color { get; set; } = string.Empty;
    }
}
