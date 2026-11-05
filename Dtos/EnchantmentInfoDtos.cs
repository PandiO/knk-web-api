using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // PandiO/knk-workspace#221: the public info sheets of the custom enchantments and abilities (web app → Guides).
    // Every number is the live combat value (#151/#156/#201/#223), worked out per enchantment level the way the plugin
    // does it; the descriptive facts (trigger, target, potions) come from EnchantmentInfoCatalog.

    /// <summary>One custom enchantment or ability as players see it on the info page.</summary>
    public class EnchantmentInfoDto
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("maxLevel")]
        public int MaxLevel { get; set; }

        /// <summary>How it goes off: <c>OnHit</c> (a melee hit, by chance), <c>RightClick</c> (an ability, always),
        /// <c>GateHit</c> (hits on gate doors), <c>Unknown</c> (a custom definition the plugin has no effect for).</summary>
        [JsonPropertyName("trigger")]
        public string Trigger { get; set; } = string.Empty;

        /// <summary>Whom it affects: <c>Target</c> (the one you hit), <c>Self</c>, <c>Area</c> (everyone around you),
        /// <c>Gate</c> (gate doors), <c>Unknown</c>.</summary>
        [JsonPropertyName("application")]
        public string Application { get; set; } = string.Empty;

        /// <summary>One or two sentences: what it does.</summary>
        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        /// <summary>Area abilities: entities up to this many blocks away take the area damage.</summary>
        [JsonPropertyName("damageRadiusBlocks")]
        public double? DamageRadiusBlocks { get; set; }

        /// <summary>Area abilities: entities up to this many blocks away are knocked back (Flash Chaos: and debuffed).</summary>
        [JsonPropertyName("knockbackRadiusBlocks")]
        public double? KnockbackRadiusBlocks { get; set; }

        /// <summary>Whether armour lowers its damage; null when it deals none.</summary>
        [JsonPropertyName("armourReducesDamage")]
        public bool? ArmourReducesDamage { get; set; }

        /// <summary>Extra facts and limits (safezones, siege, gates), one sentence each.</summary>
        [JsonPropertyName("notes")]
        public List<string> Notes { get; set; } = new();

        /// <summary>Levels I to max: chance, cooldown, effects, damage.</summary>
        [JsonPropertyName("levels")]
        public List<EnchantmentInfoLevelDto> Levels { get; set; } = new();
    }

    /// <summary>What an enchantment does at one level.</summary>
    public class EnchantmentInfoLevelDto
    {
        [JsonPropertyName("level")]
        public int Level { get; set; }

        /// <summary>Chance per hit in percent (0-100); null = it always goes off (abilities).</summary>
        [JsonPropertyName("chancePercent")]
        public double? ChancePercent { get; set; }

        /// <summary>Cooldown of the player who used it, in seconds; 0 = none.</summary>
        [JsonPropertyName("cooldownSeconds")]
        public int CooldownSeconds { get; set; }

        [JsonPropertyName("effects")]
        public List<EnchantmentInfoEffectDto> Effects { get; set; } = new();

        /// <summary>Area abilities: damage to every player and creature in the damage radius.</summary>
        [JsonPropertyName("areaDamage")]
        public double? AreaDamage { get; set; }

        /// <summary>Area abilities: damage to every gate door in reach.</summary>
        [JsonPropertyName("gateDamage")]
        public double? GateDamage { get; set; }

        /// <summary>Health restored (2 = one heart).</summary>
        [JsonPropertyName("healAmount")]
        public double? HealAmount { get; set; }

        /// <summary>A sentence for a value that isn't a combat value (Cleaving's gate numbers).</summary>
        [JsonPropertyName("detail")]
        public string? Detail { get; set; }
    }

    /// <summary>One effect at one level.</summary>
    public class EnchantmentInfoEffectDto
    {
        /// <summary>The effect as the game names it: Poison, Slowness, Frozen, ...</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Potion level (1 = I); null when the effect has no levels.</summary>
        [JsonPropertyName("potionLevel")]
        public int? PotionLevel { get; set; }

        /// <summary>How long it lasts in seconds; null when it isn't timed.</summary>
        [JsonPropertyName("durationSeconds")]
        public double? DurationSeconds { get; set; }

        /// <summary><c>Target</c>, <c>Self</c> or <c>PlayersInRange</c>.</summary>
        [JsonPropertyName("appliesTo")]
        public string AppliesTo { get; set; } = string.Empty;

        /// <summary>A short extra line, e.g. "also hides other players' name tags".</summary>
        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }
}
