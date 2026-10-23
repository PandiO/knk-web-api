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
                MatchAliases = new[] { "armorrepair", "armor_repair", "knk:armor_repair" }
            },
            new()
            {
                EnchantmentKey = "blindness",
                AbilityKey = "blindness",
                MaxLevel = 3,
                RuntimeConfigJson = "{\"abilityType\":\"ATTACK_PASSIVE\",\"trigger\":\"ON_HIT\",\"alwaysTrigger\":true,\"durationTicksPerLevel\":60,\"potion\":\"BLINDNESS\",\"amplifier\":0}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries to apply guaranteed blindness on hit.",
                PlayerUsage = "Hit enemies with the enchanted weapon to blind them.",
                EffectSummary = "Applies Blindness I for level × 60 ticks on every hit.",
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
                MatchAliases = new[] { "chaos", "knk:chaos" }
            },
            new()
            {
                EnchantmentKey = "confusion",
                AbilityKey = "confusion",
                MaxLevel = 3,
                RuntimeConfigJson = "{\"abilityType\":\"ATTACK_PASSIVE\",\"trigger\":\"ON_HIT\",\"chancePerLevel\":0.15,\"durationTicksPerLevel\":60,\"potion\":\"NAUSEA\",\"amplifier\":2}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for chance-based confusion effects.",
                PlayerUsage = "Hit enemies with the enchanted weapon for a chance to inflict nausea.",
                EffectSummary = "15% chance per level to apply Nausea III for level × 60 ticks.",
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
                MatchAliases = new[] { "flashchaos", "flash_chaos", "knk:flash_chaos" }
            },
            new()
            {
                EnchantmentKey = "freeze",
                AbilityKey = "freeze",
                MaxLevel = 3,
                RuntimeConfigJson = "{\"abilityType\":\"ATTACK_PASSIVE\",\"trigger\":\"ON_HIT\",\"chancePerLevel\":0.15,\"durationTicksPerLevel\":60,\"playersOnly\":true}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries to freeze player targets on successful trigger.",
                PlayerUsage = "Hit player targets for a chance to immobilize them briefly.",
                EffectSummary = "15% chance per level to freeze player targets for level × 60 ticks.",
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
                MatchAliases = new[] { "invisibility", "knk:invisibility" }
            },
            new()
            {
                EnchantmentKey = "poison",
                AbilityKey = "poison",
                MaxLevel = 3,
                RuntimeConfigJson = "{\"abilityType\":\"ATTACK_PASSIVE\",\"trigger\":\"ON_HIT\",\"chancePerLevel\":0.15,\"durationTicksPerLevel\":60,\"potion\":\"POISON\",\"amplifier\":1}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for chance-based poison attacks.",
                PlayerUsage = "Hit enemies with the enchanted weapon for a chance to poison them.",
                EffectSummary = "15% chance per level to apply Poison II for level × 60 ticks.",
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
                MatchAliases = new[] { "strength", "knk:strength" }
            },
            new()
            {
                EnchantmentKey = "wither",
                AbilityKey = "wither",
                MaxLevel = 3,
                RuntimeConfigJson = "{\"abilityType\":\"ATTACK_PASSIVE\",\"trigger\":\"ON_HIT\",\"chancePerLevel\":0.15,\"durationTicksPerLevel\":40,\"potion\":\"WITHER\",\"amplifier\":1}",
                FutureUserAssignmentContract = "UserAbilityAssignmentV1: supports optional per-player enable/disable and cooldown override.",
                AdminUsage = "Attach to custom EnchantmentDefinition entries for chance-based wither attacks.",
                PlayerUsage = "Hit enemies with the enchanted weapon for a chance to inflict wither.",
                EffectSummary = "15% chance per level to apply Wither II for level × 40 ticks.",
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
}