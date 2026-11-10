namespace knkwebapi_v2.Services.EnchantmentInfo
{
    /// <summary>
    /// PandiO/knk-workspace#221: what each built-in custom enchantment does, as players need to know it. These are the
    /// facts that aren't combat values: how it goes off, whom it hits, which potion at which level. They mirror knk-plugin
    /// (<c>enchantment/effects/impl/*Effect.java</c>, <c>ExecutorImpl</c>); change both together. The numbers (chance,
    /// cooldown, durations, damage, radii) come from the enchantment definition at request time.
    /// </summary>
    public static class EnchantmentInfoCatalog
    {
        public const string OnHit = "OnHit";
        public const string RightClick = "RightClick";
        public const string GateHit = "GateHit";
        public const string Unknown = "Unknown";

        public const string Target = "Target";
        public const string Self = "Self";
        public const string Area = "Area";
        public const string Gate = "Gate";
        public const string PlayersInRange = "PlayersInRange";

        /// <summary>Where an effect's potion level comes from.</summary>
        public enum LevelSource
        {
            /// <summary>No potion level (Frozen).</summary>
            None,
            /// <summary>A fixed level.</summary>
            Fixed,
            /// <summary>The enchantment's own level (Strength I/II, Resistance I/II).</summary>
            EnchantmentLevel,
            /// <summary>The combat value "effect level" (Flash Chaos's Slowness).</summary>
            EffectLevel
        }

        /// <summary>Which combat-value duration an effect lasts.</summary>
        public enum DurationSource
        {
            None,
            /// <summary>"Duration at level I", scaled by the level multiplier.</summary>
            Primary,
            /// <summary>"Second effect duration at level I", scaled by the level multiplier (Flash Chaos's Nausea).</summary>
            Secondary
        }

        public sealed record EffectFact(string Name, LevelSource LevelSource, int FixedLevel, DurationSource Duration,
            string AppliesTo, string? Note = null);

        public sealed record Facts(
            string Trigger,
            string Application,
            string Summary,
            IReadOnlyList<EffectFact> Effects,
            IReadOnlyList<string> Notes,
            bool AreaBlast = false,
            /// <summary>Health Boost: health restored per level (2 = one heart).</summary>
            double HealPerLevel = 0,
            /// <summary>Cleaving: its gate bonus and charged strike per level, from its combat values (#259).</summary>
            bool GateStrike = false);

        private const string SafezoneNote =
            "Doesn't work in combat safezones: a player in a safezone is not hit or affected.";

        private const string CooldownNote =
            "The cooldown belongs to the player who used it and counts on any target.";

        private static readonly Dictionary<string, Facts> ByKey = new(StringComparer.Ordinal)
        {
            ["poison"] = new(OnHit, Target,
                "A melee hit can poison the player or creature you hit.",
                new[] { new EffectFact("Poison", LevelSource.Fixed, 2, DurationSource.Primary, Target,
                    "Poison can't kill: it stops at half a heart.") },
                new[] { SafezoneNote, CooldownNote }),
            ["wither"] = new(OnHit, Target,
                "A melee hit can wither the player or creature you hit.",
                new[] { new EffectFact("Wither", LevelSource.Fixed, 2, DurationSource.Primary, Target,
                    "Wither can kill.") },
                new[] { SafezoneNote, CooldownNote }),
            ["freeze"] = new(OnHit, Target,
                "A melee hit can freeze the one you hit: they are held in place, also in mid-air, until it ends.",
                new[] { new EffectFact("Frozen", LevelSource.None, 0, DurationSource.Primary, Target) },
                new[] { SafezoneNote, CooldownNote, "A frozen player gets a boss bar with the time left." }),
            ["blindness"] = new(OnHit, Target,
                "A melee hit can blind the one you hit. A blinded player also sees no other player's name tag.",
                new[] { new EffectFact("Blindness", LevelSource.Fixed, 1, DurationSource.Primary, Target,
                    "Also hides other players' name tags from them.") },
                new[] { SafezoneNote, CooldownNote }),
            ["confusion"] = new(OnHit, Target,
                "A melee hit can make the one you hit nauseous (the screen wobbles).",
                new[] { new EffectFact("Nausea", LevelSource.None, 0, DurationSource.Primary, Target) },
                new[] { SafezoneNote, CooldownNote }),
            ["strength"] = new(OnHit, Self,
                "A melee hit can give you Strength, so your own hits deal more damage for a while.",
                new[] { new EffectFact("Strength", LevelSource.EnchantmentLevel, 0, DurationSource.Primary, Self) },
                new[] { SafezoneNote, CooldownNote }),
            ["cleaving"] = new(GateHit, Gate,
                "Made for sieges: your melee hits on gate doors deal more damage, and a right-click on a gate door with "
                + "the strike ready is a charged strike for much more. It does nothing to players.",
                Array.Empty<EffectFact>(),
                new[]
                {
                    "A defender's hits never hurt their own gate. The strike's cooldown is shown in a boss bar."
                },
                GateStrike: true),
            ["chaos"] = new(RightClick, Area,
                "Right-click to release a blast around you: every player and creature in the damage radius takes the "
                + "area damage, everyone in the knockback radius is thrown back, and gate doors near you take the gate "
                + "damage.",
                Array.Empty<EffectFact>(),
                new[] { SafezoneNote, CooldownNote,
                    "A kill by the blast shows \"<victim> was killed with Chaos by <player> using <item>\".",
                    "In a siege, a defender's blast doesn't hurt their own gate." },
                AreaBlast: true),
            ["flash_chaos"] = new(RightClick, Area,
                "Right-click to release a stronger blast: damage in the damage radius, and everyone in the knockback "
                + "radius is thrown back; players there are also slowed and made nauseous. Gate doors near you take the "
                + "gate damage.",
                new[]
                {
                    new EffectFact("Slowness", LevelSource.EffectLevel, 0, DurationSource.Primary, PlayersInRange),
                    new EffectFact("Nausea", LevelSource.None, 0, DurationSource.Secondary, PlayersInRange)
                },
                new[] { SafezoneNote, CooldownNote,
                    "A kill by the blast shows \"<victim> was killed with Flash Chaos by <player> using <item>\".",
                    "In a siege, a defender's blast doesn't hurt their own gate." },
                AreaBlast: true),
            ["health_boost"] = new(RightClick, Self,
                "Right-click to heal yourself over a second and a half.",
                Array.Empty<EffectFact>(),
                new[] { CooldownNote, "Healing stops if you die; it never goes above your maximum health." },
                HealPerLevel: 6),
            ["armor_repair"] = new(RightClick, Self,
                "Right-click to repair every piece of armour you wear to full durability.",
                Array.Empty<EffectFact>(),
                new[] { CooldownNote }),
            ["resistance"] = new(RightClick, Self,
                "Right-click to take less damage from every source for a while.",
                new[] { new EffectFact("Resistance", LevelSource.EnchantmentLevel, 0, DurationSource.Primary, Self) },
                new[] { CooldownNote }),
            ["invisibility"] = new(RightClick, Self,
                "Right-click to turn invisible: other players don't see you, your armour, what you hold or your name tag.",
                new[] { new EffectFact("Invisibility", LevelSource.Fixed, 1, DurationSource.Primary, Self,
                    "Siege teammates still see you; you can still be hit.") },
                new[] { CooldownNote, "Milk or death ends it early." }),
        };

        /// <summary>The facts for an enchantment key (namespace and separators ignored), or null for one without.</summary>
        public static Facts? Find(string? key)
        {
            var match = Models.AbilityDefinition.MatchKey(key ?? string.Empty);
            foreach (var (id, facts) in ByKey)
            {
                if (Models.AbilityDefinition.MatchKey(id) == match)
                {
                    return facts;
                }
            }
            return null;
        }

        /// <summary>
        /// Cleaving at one level (#259): melee hits on gate doors deal (1 + bonus x level) times the gate damage; the
        /// charged strike deals its multiplier times that hit and then needs its cooldown (0 = none) to recharge.
        /// </summary>
        public static string CleavingDetail(double bonusPercent, double strikeMultiplier, int cooldownSeconds)
        {
            var recharge = cooldownSeconds > 0 ? $", then needs {cooldownSeconds} s to recharge" : ", with no cooldown";
            return $"Melee hits on gate doors deal +{Format(bonusPercent)} % gate damage; the charged strike deals "
                   + $"{Format(strikeMultiplier)}x that hit{recharge}.";
        }

        private static string Format(double value) =>
            Math.Round(value, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
