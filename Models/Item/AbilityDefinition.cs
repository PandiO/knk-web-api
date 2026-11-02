using knkwebapi_v2.Attributes;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Text;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("AbilityDefinition")]
public class AbilityDefinition
{
    // Canonical custom-ability wiring for runtime-independent admin assignment.
    // Detailed admin/player guidance: docs/custom-enchantments/ABILITY_DEFINITION_REFERENCE.md
    public static readonly ReadOnlyCollection<AbilityCatalogEntry> CanonicalCatalog = new(
        new List<AbilityCatalogEntry>
        {
            new()
            {
                EnchantmentKey = "armor_repair",
                AbilityKey = "armor_repair",
                MaxLevel = 1,
                RuntimeConfigJson = "{\"abilityType\":\"SUPPORT_ACTIVE\",\"trigger\":\"RIGHT_CLICK\",\"cooldownMs\":120000,\"repairTarget\":\"ALL_ARMOR\",\"repairMode\":\"FULL\",\"sound\":\"ANVIL_USE\"}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for full armor repair activation on use.",
                PlayerUsage = "Right-click with the enchanted item to fully repair all equipped armor pieces.",
                EffectSummary = "Repairs all armor to full durability. Cooldown: 120s.",
                CombatDefaults = new(null, null, true, 120, null, null),
                MatchAliases = new[] { "armorrepair", "armor_repair", "knk:armor_repair" }
            },
            new()
            {
                EnchantmentKey = "blindness",
                AbilityKey = "blindness",
                MaxLevel = 3,
                RuntimeConfigJson = "{\"abilityType\":\"ATTACK_PASSIVE\",\"trigger\":\"ON_HIT\",\"potion\":\"BLINDNESS\",\"amplifier\":0}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries to blind the target on a share of hits.",
                PlayerUsage = "Hit enemies with the enchanted weapon for a chance to blind them.",
                EffectSummary = "Default: 20% chance at level I, +10 per level, to apply Blindness I for 2/3/4 s (levels I-III); 10 s cooldown per attacker.",
                CombatDefaults = new(20, 10, true, 10, 2, 0.5),
                MatchAliases = new[] { "blindness", "knk:blindness" }
            },
            new()
            {
                EnchantmentKey = "chaos",
                AbilityKey = "chaos",
                MaxLevel = 1,
                RuntimeConfigJson = "{\"abilityType\":\"SUPPORT_ACTIVE\",\"trigger\":\"RIGHT_CLICK\",\"cooldownMs\":90000,\"damageRadius\":3,\"knockbackRadius\":5,\"damage\":40}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for AoE damage and knockback activation.",
                PlayerUsage = "Right-click to release an area burst that damages and knocks back nearby entities.",
                EffectSummary = "AoE burst: up to 40 damage in radius 3 and knockback in radius 5. Cooldown: 90s.",
                CombatDefaults = new(null, null, true, 90, null, null),
                MatchAliases = new[] { "chaos", "knk:chaos" }
            },
            new()
            {
                EnchantmentKey = "cleaving",
                AbilityKey = "cleaving",
                MaxLevel = 4,
                RuntimeConfigJson = "{\"abilityType\":\"ATTACK_PASSIVE\",\"trigger\":\"GATE_HIT\",\"gateDamageBonusPerLevel\":0.25,\"chargedHitMultiplierPerLevel\":[5,6,7,8],\"chargedCooldownSecondsPerLevel\":[60,50,40,30]}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for a gate-breaking weapon (siege playtest 2026-10-24). The numbers live in knk-plugin's config.yml (gates.cleaving).",
                PlayerUsage = "Hit a gate door for extra damage; right-click a gate door for a charged strike, then wait for the cooldown.",
                EffectSummary = "Gate hits x1.25/x1.5/x1.75/x2 by level; charged right-click strike of 5/6/7/8 such hits (netherite sword: 50/72/98/128 of a 500 HP gate). Cooldown: 60/50/40/30s.",
                MatchAliases = new[] { "cleaving", "knk:cleaving" }
            },
            new()
            {
                EnchantmentKey = "confusion",
                AbilityKey = "confusion",
                MaxLevel = 3,
                RuntimeConfigJson = "{\"abilityType\":\"ATTACK_PASSIVE\",\"trigger\":\"ON_HIT\",\"potion\":\"NAUSEA\",\"amplifier\":2}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for chance-based confusion effects.",
                PlayerUsage = "Hit enemies with the enchanted weapon for a chance to inflict nausea.",
                EffectSummary = "Default: 10% chance at level I, +5 per level, to apply Nausea III for 3/4.5/6 s (levels I-III); 8 s cooldown per attacker.",
                CombatDefaults = new(10, 5, true, 8, 3, 0.5),
                MatchAliases = new[] { "confusion", "knk:confusion" }
            },
            new()
            {
                EnchantmentKey = "flash_chaos",
                AbilityKey = "flash_chaos",
                MaxLevel = 1,
                RuntimeConfigJson = "{\"abilityType\":\"SUPPORT_ACTIVE\",\"trigger\":\"RIGHT_CLICK\",\"cooldownMs\":90000,\"damageRadius\":3,\"debuffRadius\":5,\"damage\":60}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for high-impact AoE burst with debuffs.",
                PlayerUsage = "Right-click to trigger a stronger chaos burst with debuffs on nearby players.",
                EffectSummary = "AoE burst: up to 60 damage (radius 3), plus Slowness I and Nausea II to players in radius 5. Cooldown: 90s.",
                CombatDefaults = new(null, null, true, 90, null, null),
                MatchAliases = new[] { "flashchaos", "flash_chaos", "knk:flash_chaos" }
            },
            new()
            {
                EnchantmentKey = "freeze",
                AbilityKey = "freeze",
                MaxLevel = 3,
                RuntimeConfigJson = "{\"abilityType\":\"ATTACK_PASSIVE\",\"trigger\":\"ON_HIT\"}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries to freeze player targets on successful trigger.",
                PlayerUsage = "Hit player targets for a chance to immobilize them briefly.",
                EffectSummary = "Default: 10% chance at level I, +5 per level, to freeze the target for 1.5/2.25/3 s (levels I-III); then 20 s cooldown per attacker.",
                CombatDefaults = new(10, 5, true, 20, 1.5, 0.5),
                MatchAliases = new[] { "freeze", "knk:freeze" }
            },
            new()
            {
                EnchantmentKey = "health_boost",
                AbilityKey = "health_boost",
                MaxLevel = 1,
                RuntimeConfigJson = "{\"abilityType\":\"SUPPORT_ACTIVE\",\"trigger\":\"RIGHT_CLICK\",\"cooldownMs\":120000,\"iterations\":6,\"ticksBetweenIterations\":5,\"healthPerIterationPerLevel\":1}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for multi-tick self-healing bursts.",
                PlayerUsage = "Right-click to trigger a short burst of repeated healing ticks.",
                EffectSummary = "Heals in 6 pulses (every 5 ticks), scaling by level. Cooldown: 120s.",
                CombatDefaults = new(null, null, true, 120, null, null),
                MatchAliases = new[] { "healthboost", "health_boost", "knk:health_boost" }
            },
            new()
            {
                EnchantmentKey = "invisibility",
                AbilityKey = "invisibility",
                MaxLevel = 1,
                RuntimeConfigJson = "{\"abilityType\":\"SUPPORT_ACTIVE\",\"trigger\":\"RIGHT_CLICK\",\"cooldownMs\":90000,\"durationTicks\":200,\"potion\":\"INVISIBILITY\",\"amplifier\":0}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for temporary invisibility activation.",
                PlayerUsage = "Right-click to become invisible for a short duration.",
                EffectSummary = "Applies Invisibility for 200 ticks. Cooldown: 90s.",
                CombatDefaults = new(null, null, true, 90, 10, 0),
                MatchAliases = new[] { "invisibility", "knk:invisibility" }
            },
            new()
            {
                EnchantmentKey = "poison",
                AbilityKey = "poison",
                MaxLevel = 3,
                RuntimeConfigJson = "{\"abilityType\":\"ATTACK_PASSIVE\",\"trigger\":\"ON_HIT\",\"potion\":\"POISON\",\"amplifier\":1}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for chance-based poison attacks.",
                PlayerUsage = "Hit enemies with the enchanted weapon for a chance to poison them.",
                EffectSummary = "Default: 10% chance at level I, +5 per level, to apply Poison II for 3/4.5/6 s (levels I-III); 8 s cooldown per attacker.",
                CombatDefaults = new(10, 5, true, 8, 3, 0.5),
                MatchAliases = new[] { "poison", "knk:poison" }
            },
            new()
            {
                EnchantmentKey = "resistance",
                AbilityKey = "resistance",
                MaxLevel = 2,
                RuntimeConfigJson = "{\"abilityType\":\"SUPPORT_ACTIVE\",\"trigger\":\"RIGHT_CLICK\",\"cooldownMs\":120000,\"durationTicksBase\":100,\"durationTicksPerLevel\":100,\"potion\":\"DAMAGE_RESISTANCE\"}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for temporary resistance activation.",
                PlayerUsage = "Right-click to gain temporary damage resistance.",
                EffectSummary = "Applies Resistance for (level × 100 + 100) ticks. Cooldown: 120s.",
                CombatDefaults = new(null, null, true, 120, 10, 0.5),
                MatchAliases = new[] { "resistance", "knk:resistance" }
            },
            new()
            {
                EnchantmentKey = "strength",
                AbilityKey = "strength",
                MaxLevel = 2,
                RuntimeConfigJson = "{\"abilityType\":\"ATTACK_PASSIVE\",\"trigger\":\"ON_HIT\",\"chancePerLevel\":0.15,\"durationTicks\":300,\"potion\":\"INCREASE_DAMAGE\",\"appliesTo\":\"ATTACKER\",\"cooldownMs\":120000}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for chance-based self-strength buff on hit.",
                PlayerUsage = "Hit enemies for a chance to empower yourself with temporary strength.",
                EffectSummary = "15% chance per level to apply Strength to attacker for 300 ticks. Cooldown: 120s.",
                CombatDefaults = new(15, 15, true, 120, 15, 0),
                MatchAliases = new[] { "strength", "knk:strength" }
            },
            new()
            {
                EnchantmentKey = "wither",
                AbilityKey = "wither",
                MaxLevel = 3,
                RuntimeConfigJson = "{\"abilityType\":\"ATTACK_PASSIVE\",\"trigger\":\"ON_HIT\",\"potion\":\"WITHER\",\"amplifier\":1}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for chance-based wither attacks.",
                PlayerUsage = "Hit enemies with the enchanted weapon for a chance to inflict wither.",
                EffectSummary = "Default: 10% chance at level I, +5 per level, to apply Wither II for 2/3/4 s (levels I-III); 8 s cooldown per attacker.",
                CombatDefaults = new(10, 5, true, 8, 2, 0.5),
                MatchAliases = new[] { "wither", "knk:wither" }
            }
        });

    public int Id { get; set; }

    [NavigationPair(nameof(EnchantmentDefinition))]
    [RelatedEntityField(typeof(EnchantmentDefinition))]
    public int EnchantmentDefinitionId { get; set; }

    [RelatedEntityField(typeof(EnchantmentDefinition))]
    public EnchantmentDefinition EnchantmentDefinition { get; set; } = null!;

    public string AbilityKey { get; set; } = string.Empty;
    public string? RuntimeConfigJson { get; set; }
    public string? FutureUserAssignmentContract { get; set; }

    public static async Task SeedCanonicalAsync(KnKDbContext context, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var enchantments = await context.EnchantmentDefinitions
            .ToListAsync(cancellationToken);

        var createdEnchantments = 0;
        var updatedEnchantments = 0;

        foreach (var entry in CanonicalCatalog)
        {
            var enchantment = ResolveEnchantment(enchantments, entry);
            if (enchantment == null)
            {
                enchantment = new EnchantmentDefinition
                {
                    Key = entry.AbilityKey,
                    DisplayName = ToDisplayName(entry.EnchantmentKey),
                    Description = entry.PlayerUsage,
                    IsCustom = true,
                    MaxLevel = entry.MaxLevel
                };
                FillCombatDefaults(enchantment, entry);

                await context.EnchantmentDefinitions.AddAsync(enchantment, cancellationToken);
                enchantments.Add(enchantment);
                createdEnchantments++;
                continue;
            }

            var enchantmentChanged = false;

            if (!string.Equals(enchantment.Key, entry.AbilityKey, StringComparison.Ordinal))
            {
                enchantment.Key = entry.AbilityKey;
                enchantmentChanged = true;
            }

            var displayName = ToDisplayName(entry.EnchantmentKey);
            if (!string.Equals(enchantment.DisplayName, displayName, StringComparison.Ordinal))
            {
                enchantment.DisplayName = displayName;
                enchantmentChanged = true;
            }

            if (!string.Equals(enchantment.Description, entry.PlayerUsage, StringComparison.Ordinal))
            {
                enchantment.Description = entry.PlayerUsage;
                enchantmentChanged = true;
            }

            if (!enchantment.IsCustom)
            {
                enchantment.IsCustom = true;
                enchantmentChanged = true;
            }

            if (enchantment.MaxLevel != entry.MaxLevel)
            {
                enchantment.MaxLevel = entry.MaxLevel;
                enchantmentChanged = true;
            }

            if (FillCombatDefaults(enchantment, entry))
            {
                enchantmentChanged = true;
            }

            if (enchantmentChanged)
            {
                updatedEnchantments++;
            }
        }

        if (createdEnchantments > 0 || updatedEnchantments > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            enchantments = await context.EnchantmentDefinitions
                .ToListAsync(cancellationToken);
        }

        var existingByEnchantmentId = await context.AbilityDefinitions
            .ToDictionaryAsync(a => a.EnchantmentDefinitionId, cancellationToken);

        var created = 0;
        var updated = 0;
        foreach (var entry in CanonicalCatalog)
        {
            var enchantment = ResolveEnchantment(enchantments, entry);
            if (enchantment == null)
            {
                logger?.LogWarning("AbilityDefinition seed could not resolve EnchantmentDefinition for key {EnchantmentKey}.", entry.EnchantmentKey);
                continue;
            }

            if (!existingByEnchantmentId.TryGetValue(enchantment.Id, out var ability))
            {
                await context.AbilityDefinitions.AddAsync(new AbilityDefinition
                {
                    EnchantmentDefinitionId = enchantment.Id,
                    AbilityKey = entry.AbilityKey,
                    RuntimeConfigJson = entry.RuntimeConfigJson,
                    FutureUserAssignmentContract = entry.FutureUserAssignmentContract
                }, cancellationToken);

                created++;
                continue;
            }

            var hasChanges = false;
            if (!string.Equals(ability.AbilityKey, entry.AbilityKey, StringComparison.Ordinal))
            {
                ability.AbilityKey = entry.AbilityKey;
                hasChanges = true;
            }

            if (!string.Equals(ability.RuntimeConfigJson, entry.RuntimeConfigJson, StringComparison.Ordinal))
            {
                ability.RuntimeConfigJson = entry.RuntimeConfigJson;
                hasChanges = true;
            }

            if (!string.Equals(ability.FutureUserAssignmentContract, entry.FutureUserAssignmentContract, StringComparison.Ordinal))
            {
                ability.FutureUserAssignmentContract = entry.FutureUserAssignmentContract;
                hasChanges = true;
            }

            if (hasChanges)
            {
                updated++;
            }
        }

        if (created > 0 || updated > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        logger?.LogInformation(
            "Canonical seed complete. Enchantments created: {CreatedEnchantments}, updated: {UpdatedEnchantments}. Abilities created: {CreatedAbilities}, updated: {UpdatedAbilities}",
            createdEnchantments,
            updatedEnchantments,
            created,
            updated);
    }

    /// <summary>
    /// PandiO/knk-workspace#156: writes the entry's default combat values into the enchantment's empty columns. A value
    /// an admin set (anything not null) stays; clearing a field in the web app resets it to the default on the next start.
    /// </summary>
    /// <returns>true when a column was filled</returns>
    public static bool FillCombatDefaults(EnchantmentDefinition enchantment, AbilityCatalogEntry entry)
    {
        var defaults = entry.CombatDefaults;
        if (defaults == null)
        {
            return false;
        }

        var changed = false;
        if (enchantment.ProcChancePercent == null && defaults.ProcChancePercent != null)
        {
            enchantment.ProcChancePercent = defaults.ProcChancePercent;
            changed = true;
        }
        if (enchantment.ProcChancePerLevelPercent == null && defaults.ProcChancePerLevelPercent != null)
        {
            enchantment.ProcChancePerLevelPercent = defaults.ProcChancePerLevelPercent;
            changed = true;
        }
        if (enchantment.CooldownEnabled == null)
        {
            enchantment.CooldownEnabled = defaults.CooldownEnabled;
            changed = true;
        }
        if (enchantment.CooldownSeconds == null)
        {
            enchantment.CooldownSeconds = defaults.CooldownSeconds;
            changed = true;
        }
        if (enchantment.EffectDurationSeconds == null && defaults.EffectDurationSeconds != null)
        {
            enchantment.EffectDurationSeconds = defaults.EffectDurationSeconds;
            changed = true;
        }
        if (enchantment.LevelMultiplier == null && defaults.LevelMultiplier != null)
        {
            enchantment.LevelMultiplier = defaults.LevelMultiplier;
            changed = true;
        }
        return changed;
    }

    private static EnchantmentDefinition? ResolveEnchantment(
        IReadOnlyCollection<EnchantmentDefinition> customEnchantments,
        AbilityCatalogEntry entry)
    {
        var aliases = BuildAliasSet(entry);
        return customEnchantments.FirstOrDefault(e => aliases.Contains(MatchKey(e.Key)));
    }

    private static HashSet<string> BuildAliasSet(AbilityCatalogEntry entry)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal)
        {
            MatchKey(entry.EnchantmentKey),
            MatchKey(entry.AbilityKey)
        };

        foreach (var alias in entry.MatchAliases)
        {
            aliases.Add(MatchKey(alias));
        }

        return aliases;
    }

    /// <summary>
    /// The form custom-enchantment keys are compared in: no namespace, lower-case letters and digits only
    /// (<c>knk:flash_chaos</c>, <c>flash_chaos</c> and <c>flashchaos</c> are equal). Also used by the
    /// Minecraft catalogue scan to match old custom refs and definitions to scanned <c>knk:</c> keys (KNG-250).
    /// </summary>
    public static string MatchKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex >= 0 && colonIndex < trimmed.Length - 1)
        {
            trimmed = trimmed[(colonIndex + 1)..];
        }

        var builder = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static string ToDisplayName(string enchantmentKey)
    {
        if (string.IsNullOrWhiteSpace(enchantmentKey))
        {
            return string.Empty;
        }

        var key = enchantmentKey.Trim();
        var colonIndex = key.IndexOf(':');
        if (colonIndex >= 0 && colonIndex < key.Length - 1)
        {
            key = key[(colonIndex + 1)..];
        }

        var words = key
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());

        return string.Join(' ', words);
    }
}

public sealed class AbilityCatalogEntry
{
    public string EnchantmentKey { get; init; } = string.Empty;
    public string AbilityKey { get; init; } = string.Empty;
    public int MaxLevel { get; init; } = 1;
    public string RuntimeConfigJson { get; init; } = string.Empty;
    public string FutureUserAssignmentContract { get; init; } = string.Empty;
    public string AdminUsage { get; init; } = string.Empty;
    public string PlayerUsage { get; init; } = string.Empty;
    public string EffectSummary { get; init; } = string.Empty;
    public string[] MatchAliases { get; init; } = Array.Empty<string>();

    /// <summary>
    /// PandiO/knk-workspace#156: the default combat values of the enchantment (#151), written into its empty columns at
    /// startup; values an admin set are never overwritten. Must match knk-plugin's built-in values
    /// (<c>EnchantmentTuningTable</c>), which apply while the API can't be reached.
    /// </summary>
    public EnchantmentCombatDefaults? CombatDefaults { get; init; }
}

/// <summary>Default combat values of a canonical custom enchantment (PandiO/knk-workspace#151/#156); null = not used.</summary>
public sealed record EnchantmentCombatDefaults(
    double? ProcChancePercent,
    double? ProcChancePerLevelPercent,
    bool CooldownEnabled,
    int CooldownSeconds,
    double? EffectDurationSeconds,
    double? LevelMultiplier);