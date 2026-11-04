using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The normalised look of an item (PandiO/knk-workspace#232): a shield's banner (base colour + layers, bottom to
    /// top) and an armour trim (pattern + material). What <see cref="ItemAppearanceRules"/> turns a DTO into and what
    /// the services write to an <see cref="ItemBlueprint"/> or copy onto an <see cref="ItemInstance"/>.
    /// </summary>
    public sealed record ItemAppearance(
        BannerDyeColor? ShieldBannerBaseColor,
        IReadOnlyList<(string PatternKey, BannerDyeColor Color)> ShieldBannerLayers,
        string? ArmorTrimPattern,
        string? ArmorTrimMaterial)
    {
        public static readonly ItemAppearance None = new(null, Array.Empty<(string, BannerDyeColor)>(), null, null);
    }

    /// <summary>
    /// Validation and normalisation of the shield banner / armour trim fields a blueprint create or update sends
    /// (#232). Colours accept any casing ("red", "Light_Blue"); pattern keys go through
    /// <see cref="BannerPatternKeys.Normalize"/> and trim keys through <see cref="ArmorTrimKeys"/>, so "stripe_top",
    /// "Stripe_Top" and "minecraft:stripe_top" are one key. Blank strings count as not set. An unknown colour or key
    /// is an <see cref="ArgumentException"/> (a 400), never silently dropped: an item must look the way it was
    /// authored, or the save must fail. A trim needs both its pattern and its material.
    /// </summary>
    public static class ItemAppearanceRules
    {
        public static ItemAppearance Normalize(string? shieldBannerBaseColor, IEnumerable<ItemBannerLayerDto>? shieldBannerLayers,
            string? armorTrimPattern, string? armorTrimMaterial)
        {
            var baseColor = ParseColor(shieldBannerBaseColor, "shield base colour");

            var layers = new List<(string, BannerDyeColor)>();
            var index = 0;
            foreach (var layer in shieldBannerLayers ?? Enumerable.Empty<ItemBannerLayerDto>())
            {
                index++;
                if (layer == null) throw new ArgumentException($"Shield banner layer {index} is empty.");
                var patternKey = BannerPatternKeys.Normalize(layer.PatternKey)
                    ?? throw new ArgumentException(
                        $"Shield banner layer {index}: unknown banner pattern '{layer.PatternKey}'. Use a key like " +
                        "'minecraft:stripe_top' (GET /api/BannerDesigns/pattern-keys lists them all).");
                var color = ParseColor(layer.Color, $"shield banner layer {index} colour")
                    ?? throw new ArgumentException($"Shield banner layer {index} has no colour.");
                layers.Add((patternKey, color));
            }

            // A pattern without a shield colour still needs a base: Minecraft's own default for a shield is white.
            if (baseColor == null && layers.Count > 0) baseColor = BannerDyeColor.WHITE;

            var hasPattern = !string.IsNullOrWhiteSpace(armorTrimPattern);
            var hasMaterial = !string.IsNullOrWhiteSpace(armorTrimMaterial);
            string? trimPattern = null, trimMaterial = null;
            if (hasPattern || hasMaterial)
            {
                if (!hasPattern || !hasMaterial)
                    throw new ArgumentException("An armour trim needs both a pattern and a material (or neither).");
                trimPattern = ArmorTrimKeys.NormalizePattern(armorTrimPattern)
                    ?? throw new ArgumentException(
                        $"Unknown armour trim pattern '{armorTrimPattern}'. Use a key like 'minecraft:silence' " +
                        $"({string.Join(", ", ArmorTrimKeys.Patterns.Select(p => p.Substring(ArmorTrimKeys.Namespace.Length + 1)))}).");
                trimMaterial = ArmorTrimKeys.NormalizeMaterial(armorTrimMaterial)
                    ?? throw new ArgumentException(
                        $"Unknown armour trim material '{armorTrimMaterial}'. Use a key like 'minecraft:netherite' " +
                        $"({string.Join(", ", ArmorTrimKeys.Materials.Select(m => m.Substring(ArmorTrimKeys.Namespace.Length + 1)))}).");
            }

            return new ItemAppearance(baseColor, layers, trimPattern, trimMaterial);
        }

        /// <summary>The blueprint's stored look, for copying onto an instance at mint.</summary>
        public static ItemAppearance Of(ItemBlueprint blueprint) => new(
            blueprint.ShieldBannerBaseColor,
            (blueprint.ShieldBannerLayers ?? Enumerable.Empty<ItemBlueprintBannerLayer>())
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                .Select(l => (l.PatternKey, l.Color)).ToList(),
            blueprint.ArmorTrimPattern,
            blueprint.ArmorTrimMaterial);

        /// <summary>The instance's frozen look, for copying onto its clones.</summary>
        public static ItemAppearance Of(ItemInstance instance) => new(
            instance.ShieldBannerBaseColor,
            (instance.ShieldBannerLayers ?? Enumerable.Empty<ItemInstanceBannerLayer>())
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                .Select(l => (l.PatternKey, l.Color)).ToList(),
            instance.ArmorTrimPattern,
            instance.ArmorTrimMaterial);

        /// <summary>Writes the look to a blueprint, replacing its layers as a whole (SortOrder 0..n-1, bottom first).</summary>
        public static void Apply(ItemBlueprint blueprint, ItemAppearance appearance)
        {
            blueprint.ShieldBannerBaseColor = appearance.ShieldBannerBaseColor;
            blueprint.ArmorTrimPattern = appearance.ArmorTrimPattern;
            blueprint.ArmorTrimMaterial = appearance.ArmorTrimMaterial;
            blueprint.ShieldBannerLayers ??= new List<ItemBlueprintBannerLayer>();
            blueprint.ShieldBannerLayers.Clear();
            var order = 0;
            foreach (var (patternKey, color) in appearance.ShieldBannerLayers)
            {
                blueprint.ShieldBannerLayers.Add(new ItemBlueprintBannerLayer
                {
                    ItemBlueprintId = blueprint.Id,
                    SortOrder = order++,
                    PatternKey = patternKey,
                    Color = color,
                });
            }
        }

        /// <summary>Writes the look to a (new) instance; the layers are the instance's own rows.</summary>
        public static void Apply(ItemInstance instance, ItemAppearance appearance)
        {
            instance.ShieldBannerBaseColor = appearance.ShieldBannerBaseColor;
            instance.ArmorTrimPattern = appearance.ArmorTrimPattern;
            instance.ArmorTrimMaterial = appearance.ArmorTrimMaterial;
            instance.ShieldBannerLayers ??= new List<ItemInstanceBannerLayer>();
            instance.ShieldBannerLayers.Clear();
            var order = 0;
            foreach (var (patternKey, color) in appearance.ShieldBannerLayers)
            {
                instance.ShieldBannerLayers.Add(new ItemInstanceBannerLayer
                {
                    ItemInstance = instance,
                    SortOrder = order++,
                    PatternKey = patternKey,
                    Color = color,
                });
            }
        }

        /// <summary>The layers as DTOs, bottom to top.</summary>
        public static List<ItemBannerLayerDto> ToDtos(ItemAppearance appearance) => appearance.ShieldBannerLayers
            .Select(l => new ItemBannerLayerDto { PatternKey = l.PatternKey, Color = l.Color.ToString() })
            .ToList();

        private static BannerDyeColor? ParseColor(string? raw, string what)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var normalized = raw.Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
            if (Enum.TryParse<BannerDyeColor>(normalized, out var color) && Enum.IsDefined(color)) return color;
            throw new ArgumentException(
                $"Unknown {what} '{raw}'. Use a dye colour name: {string.Join(", ", Enum.GetNames<BannerDyeColor>())}.");
        }
    }
}
