using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#156: the canonical custom enchantments get weaker default combat values (#151 columns), written
/// at startup into empty columns only, so what an admin set stays.
/// </summary>
public class EnchantmentCombatDefaultsSeedTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    /// <summary>
    /// The defaults per enchantment. knk-plugin's built-in values (EnchantmentTuningTable, used while the API can't be
    /// reached) are the same numbers; change both together.
    /// </summary>
    public static IEnumerable<object?[]> Defaults() => new List<object?[]>
    {
        //            key            chance %  per lvl  cd on  cd s  duration s  multiplier
        new object?[] { "poison",       10d,     5d,    true,   8,   3d,         0.5d },
        new object?[] { "wither",       10d,     5d,    true,   8,   2d,         0.5d },
        new object?[] { "confusion",    10d,     5d,    true,   8,   3d,         0.5d },
        new object?[] { "blindness",    20d,     10d,   true,   10,  2d,         0.5d },
        new object?[] { "freeze",       10d,     5d,    true,   20,  1.5d,       0.5d },
        new object?[] { "strength",     15d,     15d,   true,   120, 15d,        0d },
        new object?[] { "chaos",        null,    null,  true,   90,  null,       null },
        new object?[] { "flash_chaos",  null,    null,  true,   90,  null,       null },
        new object?[] { "health_boost", null,    null,  true,   120, null,       null },
        new object?[] { "armor_repair", null,    null,  true,   120, null,       null },
        new object?[] { "resistance",   null,    null,  true,   120, 10d,        0.5d },
        new object?[] { "invisibility", null,    null,  true,   90,  10d,        0d },
    };

    [Theory]
    [MemberData(nameof(Defaults))]
    public async Task AFreshDatabase_GetsTheDefaults(string key, double? chance, double? perLevel, bool cooldownOn,
        int cooldownSeconds, double? duration, double? multiplier)
    {
        await using (var context = NewContext()) await AbilityDefinition.SeedCanonicalAsync(context);

        await using var read = NewContext();
        var definition = await read.EnchantmentDefinitions.SingleAsync(e => e.Key == key);
        Assert.Equal(chance, definition.ProcChancePercent);
        Assert.Equal(perLevel, definition.ProcChancePerLevelPercent);
        Assert.Equal(cooldownOn, definition.CooldownEnabled);
        Assert.Equal(cooldownSeconds, definition.CooldownSeconds);
        Assert.Equal(duration, definition.EffectDurationSeconds);
        Assert.Equal(multiplier, definition.LevelMultiplier);
    }

    [Fact]
    public async Task CleavingKeepsItsNumbersInThePluginConfig()
    {
        await using (var context = NewContext()) await AbilityDefinition.SeedCanonicalAsync(context);

        await using var read = NewContext();
        var cleaving = await read.EnchantmentDefinitions.SingleAsync(e => e.Key == "cleaving");
        Assert.Null(cleaving.ProcChancePercent);
        Assert.Null(cleaving.CooldownEnabled);
        Assert.Null(cleaving.CooldownSeconds);
    }

    [Fact]
    public async Task ValuesAnAdminSetStay_AndOnlyEmptyColumnsAreFilled()
    {
        await using (var context = NewContext())
        {
            context.EnchantmentDefinitions.Add(new EnchantmentDefinition
            {
                Key = "knk:freeze",
                DisplayName = "Freeze",
                Description = "x",
                IsCustom = true,
                MaxLevel = 3,
                ProcChancePercent = 50,
                CooldownEnabled = false,
                LevelMultiplier = 0
            });
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext()) await AbilityDefinition.SeedCanonicalAsync(context);

        await using var read = NewContext();
        var freeze = await read.EnchantmentDefinitions.SingleAsync(e => e.Key == "freeze");
        Assert.Equal(50, freeze.ProcChancePercent);
        Assert.False(freeze.CooldownEnabled);
        Assert.Equal(0, freeze.LevelMultiplier);
        Assert.Equal(5, freeze.ProcChancePerLevelPercent);
        Assert.Equal(20, freeze.CooldownSeconds);
        Assert.Equal(1.5, freeze.EffectDurationSeconds);
    }

    [Fact]
    public async Task ASecondStartChangesNothing()
    {
        await using (var context = NewContext()) await AbilityDefinition.SeedCanonicalAsync(context);

        await using var context2 = NewContext();
        var definitions = await context2.EnchantmentDefinitions.ToListAsync();
        foreach (var entry in AbilityDefinition.CanonicalCatalog)
        {
            var definition = definitions.Single(d => AbilityDefinition.MatchKey(d.Key) == AbilityDefinition.MatchKey(entry.EnchantmentKey));
            Assert.False(AbilityDefinition.FillCombatDefaults(definition, entry), entry.EnchantmentKey);
        }
    }

    [Fact]
    public void EveryCanonicalEnchantmentButCleavingHasDefaults()
    {
        Assert.All(AbilityDefinition.CanonicalCatalog, entry =>
            Assert.Equal(entry.EnchantmentKey != "cleaving", entry.CombatDefaults != null));
        Assert.Equal(AbilityDefinition.CanonicalCatalog.Count - 1, Defaults().Count());
    }

    [Fact]
    public void TheDefaultsPassTheServicesRangeChecks()
    {
        foreach (var entry in AbilityDefinition.CanonicalCatalog.Where(e => e.CombatDefaults != null))
        {
            var d = entry.CombatDefaults!;
            knkwebapi_v2.Services.EnchantmentDefinitionService.ValidateCombatValues(
                d.ProcChancePercent, d.ProcChancePerLevelPercent, d.CooldownSeconds, d.EffectDurationSeconds, d.LevelMultiplier);
        }
    }
}
