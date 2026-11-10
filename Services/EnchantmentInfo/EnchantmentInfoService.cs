using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;

namespace knkwebapi_v2.Services.EnchantmentInfo
{
    public interface IEnchantmentInfoService
    {
        /// <summary>The info sheets of every custom enchantment, by display name.</summary>
        Task<List<EnchantmentInfoDto>> GetCustomEnchantmentInfoAsync();
    }

    /// <summary>
    /// PandiO/knk-workspace#221: builds the public info sheets from the enchantment definitions' live combat values.
    /// A value an admin cleared counts as its default (<see cref="AbilityDefinition.CanonicalCatalog"/>), as in the
    /// plugin until the API's next start fills it in. Per level the numbers follow knk-plugin's
    /// <c>EnchantmentTuning</c>: chance = level-I chance + (n - 1) x per level, kept between 0 and 100; durations in
    /// whole ticks, scaled by 1 + multiplier x (n - 1); area and gate damage scaled the same way (#223).
    /// </summary>
    public class EnchantmentInfoService : IEnchantmentInfoService
    {
        private readonly IEnchantmentDefinitionRepository _repository;

        public EnchantmentInfoService(IEnchantmentDefinitionRepository repository)
        {
            _repository = repository;
        }

        public async Task<List<EnchantmentInfoDto>> GetCustomEnchantmentInfoAsync()
        {
            var definitions = await _repository.GetAllAsync();
            return definitions
                .Where(d => d.IsCustom)
                .Select(Build)
                .OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>One definition's info sheet.</summary>
        public static EnchantmentInfoDto Build(EnchantmentDefinition definition)
        {
            var facts = EnchantmentInfoCatalog.Find(definition.Key);
            var defaults = DefaultsFor(definition.Key);
            var values = CombatValues.Of(definition, defaults);
            var maxLevel = Math.Max(1, definition.MaxLevel);

            var sheet = new EnchantmentInfoDto
            {
                Key = definition.Key,
                DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.Key : definition.DisplayName,
                Description = definition.Description,
                MaxLevel = maxLevel,
                Trigger = facts?.Trigger ?? EnchantmentInfoCatalog.Unknown,
                Application = facts?.Application ?? EnchantmentInfoCatalog.Unknown,
                Summary = facts?.Summary ?? definition.Description,
                Notes = facts?.Notes.ToList() ?? new List<string>()
            };

            if (facts?.AreaBlast == true)
            {
                sheet.DamageRadiusBlocks = values.DamageRadius;
                // The plugin reaches at least as far as the damage radius (AreaBlast.reach()).
                sheet.KnockbackRadiusBlocks = Math.Max(values.DamageRadius, values.EffectRadius);
                sheet.ArmourReducesDamage = true;
            }

            for (var level = 1; level <= maxLevel; level++)
            {
                sheet.Levels.Add(BuildLevel(facts, values, level));
            }
            return sheet;
        }

        private static EnchantmentInfoLevelDto BuildLevel(EnchantmentInfoCatalog.Facts? facts, CombatValues values, int level)
        {
            var scale = 1.0 + values.LevelMultiplier * Math.Max(0, level - 1);
            var row = new EnchantmentInfoLevelDto
            {
                Level = level,
                CooldownSeconds = values.CooldownEnabled ? values.CooldownSeconds : 0,
                // Abilities always go off; only on-hit enchantments roll a chance.
                ChancePercent = facts?.Trigger == EnchantmentInfoCatalog.OnHit
                    ? Math.Round(Math.Clamp(values.ChancePercent + values.ChancePerLevelPercent * (level - 1), 0, 100), 2)
                    : null
            };
            if (facts == null)
            {
                return row;
            }

            foreach (var effect in facts.Effects)
            {
                var durationTicks = effect.Duration switch
                {
                    EnchantmentInfoCatalog.DurationSource.Primary => ScaledTicks(values.DurationSeconds, scale),
                    EnchantmentInfoCatalog.DurationSource.Secondary => ScaledTicks(values.SecondaryDurationSeconds, scale),
                    _ => (int?)null
                };
                if (durationTicks == 0)
                {
                    continue; // A duration of 0 leaves the effect out, as in the plugin.
                }
                row.Effects.Add(new EnchantmentInfoEffectDto
                {
                    Name = effect.Name,
                    PotionLevel = effect.LevelSource switch
                    {
                        EnchantmentInfoCatalog.LevelSource.Fixed => effect.FixedLevel,
                        EnchantmentInfoCatalog.LevelSource.EnchantmentLevel => level,
                        EnchantmentInfoCatalog.LevelSource.EffectLevel => values.EffectLevel,
                        _ => null
                    },
                    DurationSeconds = durationTicks.HasValue ? durationTicks.Value / 20.0 : null,
                    AppliesTo = effect.AppliesTo,
                    Note = effect.Note
                });
            }

            if (facts.AreaBlast)
            {
                row.AreaDamage = Math.Round(values.AreaDamage * scale, 2);
                row.GateDamage = Math.Round(values.GateDamage * scale, 2);
            }
            if (facts.HealPerLevel > 0)
            {
                row.HealAmount = facts.HealPerLevel * level;
            }
            row.Detail = facts.LevelDetail?.Invoke(level);
            return row;
        }

        /// <summary>Seconds to whole ticks, scaled and rounded like the plugin (EnchantmentTuning.durationTicksAt).</summary>
        private static int ScaledTicks(double seconds, double scale)
        {
            var baseTicks = (int)Math.Round(seconds * 20.0, MidpointRounding.AwayFromZero);
            return (int)Math.Round(baseTicks * scale, MidpointRounding.AwayFromZero);
        }

        private static EnchantmentCombatDefaults? DefaultsFor(string? key)
        {
            var match = AbilityDefinition.MatchKey(key ?? string.Empty);
            return AbilityDefinition.CanonicalCatalog.FirstOrDefault(e =>
                AbilityDefinition.MatchKey(e.EnchantmentKey) == match
                || AbilityDefinition.MatchKey(e.AbilityKey) == match
                || e.MatchAliases.Any(a => AbilityDefinition.MatchKey(a) == match))?.CombatDefaults;
        }

        /// <summary>The values in effect: the admin's, else the default, else the plugin's fallback for none (0).</summary>
        private sealed record CombatValues(
            double ChancePercent, double ChancePerLevelPercent, bool CooldownEnabled, int CooldownSeconds,
            double DurationSeconds, double LevelMultiplier, int EffectLevel, double SecondaryDurationSeconds,
            double AreaDamage, double GateDamage, double DamageRadius, double EffectRadius)
        {
            public static CombatValues Of(EnchantmentDefinition d, EnchantmentCombatDefaults? x) => new(
                d.ProcChancePercent ?? x?.ProcChancePercent ?? 100,
                d.ProcChancePerLevelPercent ?? x?.ProcChancePerLevelPercent ?? 0,
                d.CooldownEnabled ?? x?.CooldownEnabled ?? false,
                d.CooldownSeconds ?? x?.CooldownSeconds ?? 0,
                d.EffectDurationSeconds ?? x?.EffectDurationSeconds ?? 0,
                Math.Max(0, d.LevelMultiplier ?? x?.LevelMultiplier ?? 0),
                Math.Clamp(d.EffectLevel ?? x?.EffectLevel ?? 1, 1, EnchantmentDefinitionService.MaxEffectLevel),
                d.SecondaryEffectDurationSeconds ?? x?.SecondaryEffectDurationSeconds ?? 0,
                d.AreaDamage ?? x?.AreaDamage ?? 0,
                d.GateDamage ?? x?.GateDamage ?? 0,
                d.DamageRadiusBlocks ?? x?.DamageRadiusBlocks ?? 0,
                d.EffectRadiusBlocks ?? x?.EffectRadiusBlocks ?? 0);
        }
    }
}
